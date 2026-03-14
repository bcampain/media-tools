using MediaTools.Domain.FileMapping;
using MediaTools.Domain.Models;
using MediaTools.Infrastructure.Logging;
using MediaTools.Infrastructure.Notifications;
using MediaTools.Scripts;

namespace MediaTools.App.Promote;

/// <summary>
/// Native C# implementation of the promote step.
/// Replaces the old PromoteRunner that delegated to /usr/local/bin/promote.
/// </summary>
public class NativePromoteRunner(IDiscordNotifier discord) : IPromoteRunner
{
    private static readonly string[] OriginalExtensions = [".mkv", ".mp4", ".m4v"];

    public async Task<int> RunAsync(
        string                    target,
        PipelineRun               run,
        PromoteScriptOptions      options,
        Action<StepFileProgress>? onProgress,
        ILogSink                  log,
        CancellationToken         ct)
    {
        var kindSegment = run.Kind.ToString().ToLowerInvariant();
        var archiveBase = Path.Combine(run.StagingRoot, "archive", kindSegment);
        var incomingBase = Path.Combine(run.IncomingRoot, kindSegment);
        var targetIsFile = File.Exists(target);
        var targetIsDir = Directory.Exists(target);
        if (!targetIsFile && !targetIsDir)
        {
            log.Error($"[promote] Target path does not exist: {target}");
            return 2;
        }

        var targetMode = targetIsFile ? TargetMode.File : TargetMode.Dir;
        var targetLabel = run.Kind == Kind.Tv ? "Show" : "Kind";
        var labelValue = run.Kind == Kind.Tv
            ? Path.GetFileName(target.TrimEnd(Path.DirectorySeparatorChar))
            : kindSegment;

        log.Info($"[promote] run_id={run.RunId}");
        log.Info($"[promote] target={target}");
        log.Info($"[promote] kind={kindSegment}");
        log.Info($"[promote] mode={targetMode.ToString().ToLowerInvariant()}");
        log.Info($"[promote] retention_days={options.RetentionDays}");
        log.Info($"[promote] overwrite={options.Overwrite}");
        log.Info($"[promote] dry_run={options.DryRun}");

        var startMessage = run.Kind == Kind.Tv
            ? $"{targetLabel}: {labelValue} | run_id={run.RunId}"
            : $"{targetLabel}: {labelValue} | run_id={run.RunId}\nTarget: {target}";
        await discord.NotifyAsync(
            "📚 Step 3 of 3: Promote to Library started",
            startMessage,
            null, ct);

        if (targetMode == TargetMode.File &&
            target.EndsWith(".norm.mp4", StringComparison.OrdinalIgnoreCase))
        {
            log.Error("[promote] Refusing to promote a .norm.mp4 file. Normalize first.");
            return 2;
        }

        if (targetMode == TargetMode.Dir && HasRemainingNormFiles(target))
        {
            log.Error("[promote] .norm.mp4 files remain under target. Run normalize_audio first.");
            var msg = run.Kind == Kind.Tv
                ? $"{targetLabel}: {labelValue} | run_id={run.RunId}\n.norm.mp4 files remain under staging show root. Run normalize_audio first."
                : $"{targetLabel}: {labelValue} | run_id={run.RunId}\n.norm.mp4 files remain under target. Run normalize_audio first.\nTarget: {target}";
            await discord.NotifyAsync("⚠️ Step 3 of 3: Promote to Library finished", msg, run.LogFile, ct);
            return 3;
        }

        var inputFiles = targetMode == TargetMode.File
            ? [target]
            : DiscoverPromoteFiles(target);

        if (inputFiles.Count == 0)
        {
            log.Warn("[promote] No final .mp4 files found to promote. Nothing to do.");
            var msg = run.Kind == Kind.Tv
                ? $"{targetLabel}: {labelValue} | run_id={run.RunId}\nNo final mp4 files found to promote. Nothing to do."
                : $"{targetLabel}: {labelValue} | run_id={run.RunId}\nNo final mp4 files found to promote. Nothing to do.\nTarget: {target}";
            await discord.NotifyAsync("⚠️ Step 3 of 3: Promote to Library finished", msg, run.LogFile, ct);
            return 0;
        }

        var jobs = inputFiles
            .Select(f => new FileJobRecord
            {
                InputPath = f,
                OutputPath = VideoPathMapper.MapPromoteOutput(f, run.StagingRoot, run.LibraryRoot),
                Status = StepStatus.Pending
            })
            .ToList();

        ReportProgress(onProgress, jobs, currentFile: null);

        var failedCount = 0;
        var createdCount = 0;
        var overwrittenCount = 0;
        var promotedInputs = new List<string>(jobs.Count);

        for (var i = 0; i < jobs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var job = jobs[i];
            var fileName = Path.GetFileName(job.InputPath);
            log.Info($"[promote] [{i + 1}/{jobs.Count}] {fileName}");
            log.Info($"[promote]   input:  {job.InputPath}");
            log.Info($"[promote]   output: {job.OutputPath}");

            jobs[i] = job with { Status = StepStatus.Running, StartedAt = DateTime.UtcNow };
            ReportProgress(onProgress, jobs, currentFile: fileName);

            var outputDir = Path.GetDirectoryName(job.OutputPath)!;
            try
            {
                Directory.CreateDirectory(outputDir);
            }
            catch (Exception ex)
            {
                log.Error($"[promote]   FAILED(mkdir): {outputDir} ({ex.Message})");
                failedCount++;
                jobs[i] = jobs[i] with { Status = StepStatus.Failed, CompletedAt = DateTime.UtcNow, ExitCode = 1 };
                ReportProgress(onProgress, jobs, currentFile: null);
                continue;
            }
            var existed = File.Exists(job.OutputPath);

            if (options.DryRun)
            {
                log.Info(existed
                    ? $"[promote]   DRYRUN(overwrite): {fileName}"
                    : $"[promote]   DRYRUN: {fileName}");
                jobs[i] = jobs[i] with { Status = StepStatus.Skipped, CompletedAt = DateTime.UtcNow, ExitCode = 0 };
                ReportProgress(onProgress, jobs, currentFile: null);
                continue;
            }

            var rc = CopyToLibrary(job.InputPath, job.OutputPath, log);

            if (rc != 0)
            {
                failedCount++;
                jobs[i] = jobs[i] with { Status = StepStatus.Failed, CompletedAt = DateTime.UtcNow, ExitCode = rc };
                ReportProgress(onProgress, jobs, currentFile: null);
                continue;
            }

            promotedInputs.Add(job.InputPath);
            jobs[i] = jobs[i] with { Status = StepStatus.Complete, CompletedAt = DateTime.UtcNow, ExitCode = 0 };
            ReportProgress(onProgress, jobs, currentFile: null);

            if (existed)
            {
                overwrittenCount++;
                log.Info($"[promote]   OVERWROTE: {fileName}");
                var overwrittenRelative = Path.GetRelativePath(run.LibraryRoot, job.OutputPath);
                var overwriteMsg = $"{targetLabel}: {labelValue} | run_id={run.RunId}\nOverwrote: {overwrittenRelative}";
                await discord.NotifyAsync("♻️ Promote: Overwrote existing library file", overwriteMsg, run.LogFile, ct);
            }
            else
            {
                createdCount++;
                log.Info($"[promote]   CREATED: {fileName}");
            }
        }

        if (failedCount > 0)
        {
            log.Error($"[promote] Promote failed for {failedCount} file(s). created={createdCount} overwritten={overwrittenCount} failed={failedCount}");
            var failMsg = run.Kind == Kind.Tv
                ? $"{targetLabel}: {labelValue} | run_id={run.RunId}\nPromote failed for {failedCount} files.\nCreated: {createdCount}, Overwritten: {overwrittenCount}, Failed: {failedCount}"
                : $"{targetLabel}: {labelValue} | run_id={run.RunId}\nPromote failed for {failedCount} files.\nCreated: {createdCount}, Overwritten: {overwrittenCount}, Failed: {failedCount}";
            await discord.NotifyAsync("❌ Step 3 of 3: Promote to Library finished with errors", failMsg, run.LogFile, ct);
            return 4;
        }

        if (options.DryRun)
        {
            log.Info("[promote] archive: DRYRUN");
            log.Info($"[promote] archive_prune: DRYRUN older_than_days={options.RetentionDays}");
            log.Info("[promote] staging_cleanup: DRYRUN");
        }
        else
        {
            var archiveRc = ArchiveIncoming(target, run, promotedInputs, archiveBase, incomingBase, log);
            if (archiveRc != 0)
                return archiveRc;

            var prunedCount = PruneArchives(archiveBase, options.RetentionDays, log);
            if (prunedCount > 0)
            {
                var pruneMsg = run.Kind == Kind.Tv
                    ? $"{targetLabel}: {labelValue} | run_id={run.RunId}\nPruned {prunedCount} archives older than {options.RetentionDays} days."
                    : $"{targetLabel}: {labelValue} | run_id={run.RunId}\nPruned {prunedCount} archives older than {options.RetentionDays} days.";
                await discord.NotifyAsync("⚠️ Step 3 of 3: Promote to Library archive prune", pruneMsg, run.LogFile, ct);
            }

            CleanupStaging(target, targetMode, run.Kind, promotedInputs, log);
        }

        var doneMsg = run.Kind == Kind.Tv
            ? $"{targetLabel}: {labelValue} | run_id={run.RunId} | created={createdCount} overwritten={overwrittenCount} failed=0"
            : $"{targetLabel}: {labelValue} | run_id={run.RunId} | created={createdCount} overwritten={overwrittenCount} failed=0";

        await discord.NotifyAsync("✅ Step 3 of 3: Promote to Library finished", doneMsg, run.LogFile, ct);
        return 0;
    }

    private static bool HasRemainingNormFiles(string target) =>
        Directory.EnumerateFiles(target, "*.norm.mp4", SearchOption.AllDirectories)
            .Any(f => !Path.GetFileName(f).StartsWith("._", StringComparison.Ordinal));

    private static List<string> DiscoverPromoteFiles(string target) =>
        Directory.EnumerateFiles(target, "*.mp4", SearchOption.AllDirectories)
            .Where(f =>
            {
                var file = Path.GetFileName(f);
                return !file.StartsWith("._", StringComparison.Ordinal) &&
                       !file.EndsWith(".norm.mp4", StringComparison.OrdinalIgnoreCase) &&
                       !file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) &&
                       !file.EndsWith(".tmp.mp4", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    private static int CopyToLibrary(string src, string dest, ILogSink log)
    {
        var tmp = $"{dest}.tmp.{Environment.ProcessId}";
        try
        {
            File.Copy(src, tmp, overwrite: true);
            File.Move(tmp, dest, overwrite: true);
            return 0;
        }
        catch (Exception ex)
        {
            TryDelete(tmp);
            log.Error($"[promote]   FAILED(copy): {src} -> {dest} ({ex.Message})");
            return 1;
        }
    }

    private static int ArchiveIncoming(
        string target,
        PipelineRun run,
        List<string> promotedInputs,
        string archiveBase,
        string incomingBase,
        ILogSink log)
    {
        if (!Directory.Exists(incomingBase))
        {
            log.Info("[promote] archive: SKIPPED (incoming folder not found)");
            return 0;
        }

        var archiveDir = Path.Combine(archiveBase, $"_archived-{run.RunId}");
        Directory.CreateDirectory(archiveDir);

        try
        {
            if (run.Kind == Kind.Tv)
            {
                var showName = Path.GetFileName(target.TrimEnd(Path.DirectorySeparatorChar));
                var incomingShowRoot = Path.Combine(incomingBase, showName);
                if (!Directory.Exists(incomingShowRoot))
                {
                    log.Info("[promote] archive: SKIPPED (incoming show folder not found)");
                    return 0;
                }

                CopyDirectoryContents(incomingShowRoot, archiveDir);
                Directory.Delete(incomingShowRoot, recursive: true);
                log.Info($"[promote] archive: OK -> {archiveDir}");
                return 0;
            }

            var stageKindRoot = Path.Combine(run.StagingRoot, run.Kind.ToString().ToLowerInvariant());

            foreach (var staged in promotedInputs)
            {
                var relativeMp4 = Path.GetRelativePath(stageKindRoot, staged);
                if (!TryFindIncomingOriginal(relativeMp4, incomingBase, out var original))
                {
                    log.Warn($"[promote] archive: WARN no incoming original found for staged_rel={relativeMp4}");
                    continue;
                }

                var relativeOriginal = Path.GetRelativePath(incomingBase, original);
                var relativeDir = Path.GetDirectoryName(relativeOriginal);
                var archiveDestDir = string.IsNullOrEmpty(relativeDir)
                    ? archiveDir
                    : Path.Combine(archiveDir, relativeDir);

                Directory.CreateDirectory(archiveDestDir);
                var archiveDest = Path.Combine(archiveDestDir, Path.GetFileName(original));
                File.Copy(original, archiveDest, overwrite: true);
                File.Delete(original);
                log.Info($"[promote] archive: OK original -> {archiveDest}");
            }

            DeleteEmptyDirectories(incomingBase, stopAt: incomingBase);
            log.Info($"[promote] archive: OK -> {archiveDir}");
            return 0;
        }
        catch (Exception ex)
        {
            log.Error($"[promote] archive: FAILED ({ex.Message})");
            return 5;
        }
    }

    private static int PruneArchives(string archiveBase, int retentionDays, ILogSink log)
    {
        if (!Directory.Exists(archiveBase))
        {
            log.Info("[promote] archive_prune: none (no archive dir)");
            return 0;
        }

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var pruned = 0;

        foreach (var dir in Directory.EnumerateDirectories(archiveBase))
        {
            var name = Path.GetFileName(dir);
            if (!name.StartsWith("_archived-", StringComparison.Ordinal))
                continue;

            var lastWrite = Directory.GetLastWriteTimeUtc(dir);
            if (lastWrite >= cutoff)
                continue;

            Directory.Delete(dir, recursive: true);
            pruned++;
        }

        log.Info($"[promote] archive_prune: removed={pruned} older_than_days={retentionDays}");
        return pruned;
    }

    private static void CleanupStaging(
        string target,
        TargetMode targetMode,
        Kind kind,
        List<string> promotedInputs,
        ILogSink log)
    {
        var deletedFiles = 0;
        foreach (var f in promotedInputs)
        {
            try
            {
                if (!File.Exists(f)) continue;
                File.Delete(f);
                deletedFiles++;
            }
            catch (Exception ex)
            {
                log.Warn($"[promote] staging_cleanup: failed to delete {f}: {ex.Message}");
            }
        }

        if (kind == Kind.Tv)
        {
            try
            {
                if (Directory.Exists(target))
                    Directory.Delete(target, recursive: true);
            }
            catch (Exception ex)
            {
                log.Warn($"[promote] staging_cleanup: failed to remove show root {target}: {ex.Message}");
            }

            log.Info($"[promote] staging_cleanup: deleted_files={deletedFiles} (final mp4s)");
            log.Info($"[promote] staging_cleanup: removing_show_root={target}");
            return;
        }

        var cleanupRoot = targetMode == TargetMode.Dir
            ? target
            : Path.GetDirectoryName(target) ?? target;

        foreach (var promoted in promotedInputs)
            DeleteEmptyParents(Path.GetDirectoryName(promoted), cleanupRoot);

        log.Info($"[promote] staging_cleanup: deleted_files={deletedFiles} (final mp4s)");
    }

    private static bool TryFindIncomingOriginal(string relativeMp4, string incomingBase, out string originalPath)
    {
        var relativeDir = Path.GetDirectoryName(relativeMp4);
        var stem = Path.GetFileNameWithoutExtension(relativeMp4);
        var searchDir = string.IsNullOrEmpty(relativeDir)
            ? incomingBase
            : Path.Combine(incomingBase, relativeDir);

        foreach (var ext in OriginalExtensions)
        {
            var candidate = Path.Combine(searchDir, stem + ext);
            if (File.Exists(candidate))
            {
                originalPath = candidate;
                return true;
            }
        }

        originalPath = string.Empty;
        return false;
    }

    private static void CopyDirectoryContents(string srcDir, string destDir)
    {
        foreach (var file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(srcDir, file);
            var dest = Path.Combine(destDir, relative);
            var parent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static void DeleteEmptyDirectories(string root, string stopAt)
    {
        var dirs = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .OrderByDescending(d => d.Length)
            .ToList();

        foreach (var dir in dirs)
        {
            if (string.Equals(dir, stopAt, StringComparison.Ordinal))
                continue;

            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
    }

    private static void DeleteEmptyParents(string? startDir, string stopAtExclusive)
    {
        var current = startDir;
        while (!string.IsNullOrEmpty(current) &&
               !string.Equals(current, stopAtExclusive, StringComparison.Ordinal))
        {
            if (Directory.EnumerateFileSystemEntries(current).Any())
                break;

            Directory.Delete(current);
            current = Path.GetDirectoryName(current);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best effort cleanup
        }
    }

    private static void ReportProgress(
        Action<StepFileProgress>? onProgress,
        List<FileJobRecord> jobs,
        string? currentFile)
    {
        if (onProgress is null) return;

        var processed = 0;
        var failed = 0;
        var skipped = 0;
        foreach (var job in jobs)
        {
            if (job.Status is StepStatus.Complete or StepStatus.Failed or StepStatus.Skipped) processed++;
            if (job.Status == StepStatus.Failed) failed++;
            if (job.Status == StepStatus.Skipped) skipped++;
        }

        onProgress(new StepFileProgress
        {
            TotalFiles = jobs.Count,
            ProcessedFiles = processed,
            FailedFiles = failed,
            SkippedFiles = skipped,
            CurrentFile = currentFile,
            Files = jobs.AsReadOnly()
        });
    }
}
