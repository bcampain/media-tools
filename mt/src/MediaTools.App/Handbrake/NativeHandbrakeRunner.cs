using System.Diagnostics;
using MediaTools.App.FileSystem;
using MediaTools.Domain.FileMapping;
using MediaTools.Domain.Models;
using MediaTools.Infrastructure.Logging;
using MediaTools.Infrastructure.Notifications;
using MediaTools.Scripts;

namespace MediaTools.App.Handbrake;

/// <summary>
/// Native C# implementation of the handbrake encoding step.
/// Replaces the old HandbrakeRunner that shelled out to /usr/local/bin/handbrake_mp4.
///
/// Responsibilities:
///   1. Discover video files under the target path up to maxDepth.
///   2. Map each input file to its expected staging output path via VideoPathMapper.
///   3. Run HandBrakeCLI for each file, streaming progress to the log sink.
///   4. Report per-file progress via the onProgress callback so the pipeline handler
///      can update the manifest and the mt-dashboard can render a live progress bar.
///
/// HandBrakeCLI encoding options:
///   - Container:       MP4 (av_mp4)
///   - Video encoder:   x265 (HEVC) — best size/quality tradeoff for archival
///   - Quality (CRF):   from options.Quality (default 23 in PipelineCommandHandler)
///   - Encoder preset:  from options.Preset  (default "fast")
///   - Audio:           copy AAC/AC3/EAC3/DTS tracks; fallback-encode others to AAC
///
/// Progress is reported after each file completes (or fails), and once at the start
/// with all files as "pending" so the dashboard can show the total file count immediately.
/// </summary>
public class NativeHandbrakeRunner(
    VideoFileScanner scanner, 
    IDiscordNotifier discord, 
    ILogSink log) : IHandbrakeRunner
{
    // Ordered list of paths to check for HandBrakeCLI at startup.
    private static readonly string[] CandidatePaths =
    [
        "/usr/local/bin/HandBrakeCLI",
        "/usr/bin/HandBrakeCLI",
        "/opt/homebrew/bin/HandBrakeCLI",
        "/Applications/HandBrakeCLI",   // macOS drag-and-drop install
    ];

    // Lazy so the PATH search happens once, on first use, for the lifetime of the
    // singleton. The executable path never changes while the process is running.
    private readonly Lazy<string?> _handBrakePath = new(FindHandBrakeCLI);

    // ─────────────────────────────────────────────────────────────────────────

    public async Task<int> RunAsync(
        string                    target,
        PipelineRun               run,
        HandbrakeScriptOptions    options,
        Action<StepFileProgress>? onProgress,
        CancellationToken         ct)
    {
        var hbPath = _handBrakePath.Value;
        if (hbPath is null)
        {
            log.Error("[handbrake] HandBrakeCLI not found.");
            log.Error("[handbrake] Checked: " + string.Join(", ", CandidatePaths));
            log.Error("[handbrake] Install HandBrakeCLI and ensure it is on PATH.");
            return 1;
        }

        log.Info($"[handbrake] Using HandBrakeCLI: {hbPath}");

        // ── Discover input files ──────────────────────────────────────────────
        var inputFiles = File.Exists(target)
            ? scanner.ScanSingleFile(target)
            : scanner.Scan(target, options.MaxDepth);

        if (inputFiles.Count == 0)
        {
            log.Warn($"[handbrake] No video files found under: {target}");
            log.Warn("[handbrake] Nothing to do. Exiting with success.");
            await discord.NotifyAsync(
                "⚠️ Step 1 of 3: HandBrake finished",
                $"Target: {target} | run_id={run.RunId}\nNo source video files found, nothing to do.",
                run.LogFile, ct);
            return 0;
        }

        log.Info($"[handbrake] Found {inputFiles.Count} file(s) to encode.");

        // ── Build the job list ────────────────────────────────────────────────
        var jobs = inputFiles
            .Select(f => new FileJobRecord
            {
                InputPath  = f,
                OutputPath = VideoPathMapper.MapHandbrakeOutput(f, run.IncomingRoot, run.StagingRoot),
                Status     = StepStatus.Pending
            })
            .ToList();

        // Emit initial progress so the dashboard shows total count immediately
        ReportProgress(onProgress, jobs, currentFile: null);

        var failedCount  = 0;
        var skippedCount = 0;
        var createdCount = 0;

        for (var i = 0; i < jobs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var job = jobs[i];
            var fileName = Path.GetFileName(job.InputPath);
            log.Info($"[handbrake] [{i + 1}/{jobs.Count}] {fileName}");
            log.Info($"[handbrake]   input:  {job.InputPath}");
            log.Info($"[handbrake]   output: {job.OutputPath}");

            // Mark this file as running
            jobs[i] = job with { Status = StepStatus.Running, StartedAt = DateTime.UtcNow };
            ReportProgress(onProgress, jobs, currentFile: fileName);

            // Skip if output already exists and --force not requested
            if (!options.Force && File.Exists(job.OutputPath))
            {
                log.Info($"[handbrake]   Skipping — output already exists (use --force to re-encode).");
                jobs[i] = jobs[i] with { Status = StepStatus.Complete, CompletedAt = DateTime.UtcNow, ExitCode = 0 };
                ReportProgress(onProgress, jobs, currentFile: null);
                skippedCount++;
                continue;
            }

            // Ensure the output directory exists
            var outDir = Path.GetDirectoryName(job.OutputPath)!;
            try
            {
                Directory.CreateDirectory(outDir);
            }
            catch (Exception ex)
            {
                log.Error($"[handbrake]   Cannot create output directory {outDir}: {ex.Message}");
                jobs[i] = jobs[i] with { Status = StepStatus.Failed, CompletedAt = DateTime.UtcNow, ExitCode = 1 };
                ReportProgress(onProgress, jobs, currentFile: null);
                failedCount++;
                continue;
            }

            // ── Run HandBrakeCLI ──────────────────────────────────────────────
            var rc = await EncodeFileAsync(hbPath, job.InputPath, job.OutputPath, options, ct);

            var completedAt = DateTime.UtcNow;
            if (rc == 0)
            {
                log.Info($"[handbrake]   ✓ Done: {Path.GetFileName(job.OutputPath)}");
                
                var createdTitle = $"☑️ HandBrake: Created output mp4 ({i + 1}/{jobs.Count})";
                var createdMessage = $"""
                               Target: {target} | run_id: {run.RunId}
                               Created: {Path.GetFileName(job.OutputPath)}
                               Original: {Path.GetFileName(job.InputPath)}
                               """;
                await discord.NotifyAsync(createdTitle, createdMessage, null, ct);

                jobs[i] = jobs[i] with { Status = StepStatus.Complete, CompletedAt = completedAt, ExitCode = 0 };
                createdCount++;
            }
            else
            {
                log.Error($"[handbrake]   ✗ Failed (exit {rc}): {fileName}");

                // Remove partial output to avoid leaving a corrupt file in staging
                TryDeletePartialOutput(job.OutputPath);

                await discord.NotifyAsync(
                    $"❌ HandBrake: Failed to process file ({i + 1}/{jobs.Count})",
                    $"Target: {target} | run_id={run.RunId}\nFailed file: {fileName}",
                    run.LogFile, ct);

                jobs[i] = jobs[i] with { Status = StepStatus.Failed, CompletedAt = completedAt, ExitCode = rc };
                failedCount++;
            }

            ReportProgress(onProgress, jobs, currentFile: null);
        }

        log.Info($"[handbrake] Complete: {createdCount + skippedCount}/{jobs.Count} succeeded" +
                 (failedCount > 0 ? $", {failedCount} failed" : ""));

        var completeMessage = $"Target: {target} | run_id={run.RunId} | created={createdCount} skipped={skippedCount} failed={failedCount}";
        await discord.NotifyAsync("✅ Step 1 of 3: HandBrake finished", completeMessage, run.LogFile, ct);

        return failedCount > 0 ? 1 : 0;
    }

    // ─── Encoding ─────────────────────────────────────────────────────────────

    private async Task<int> EncodeFileAsync(
        string                 hbPath,
        string                 inputPath,
        string                 outputPath,
        HandbrakeScriptOptions options,
        CancellationToken      ct)
    {
        var args = BuildHandBrakeArgs(inputPath, outputPath, options);
        log.Info($"[handbrake]   cmd: {hbPath} {args}");

        var psi = new ProcessStartInfo(hbPath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            WorkingDirectory       = "/"
        };

        using var process = new Process { StartInfo = psi };

        // HandBrakeCLI writes encoding progress to stdout and diagnostic info to stderr.
        // We forward both to the console (same approach as the old script runners) so
        // the operator can see real-time progress in the terminal.
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            // Suppress the noisy per-frame progress lines from the log file but still
            // print them to console so the operator sees live progress in the terminal.
            Console.WriteLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            Console.Error.WriteLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        // Flush remaining buffered output before returning
        process.WaitForExit();
        return process.ExitCode;
    }

    // ─── HandBrakeCLI argument construction ──────────────────────────────────

    /// <summary>
    /// Builds the HandBrakeCLI command-line arguments.
    ///
    /// Container choice:  MP4 (av_mp4) — matches the original handbrake_mp4 script name
    ///                    and is universally compatible.
    /// Video encoder:     x265 (HEVC) — better compression than x264 at the same quality.
    /// Quality:           CRF scale (lower = better quality, larger file).
    ///                    23 is the default for x265; the original script default was also 23.
    /// Encoder preset:    Controls encode speed vs. compression efficiency.
    ///                    "fast" is a good balance for batch processing.
    /// Audio:             Copy supported tracks verbatim; fall back to AAC for others.
    ///                    This preserves Dolby/DTS tracks when the container supports them.
    /// Subtitles:         --all-subtitles selects every subtitle track from the source and
    ///                    passes them through as selectable (soft) tracks — no forced-only
    ///                    filter, nothing burned in. This mirrors the HandBrake GUI behavior
    ///                    of "Add All Tracks" without checking "Forced Only" or "Burned In".
    ///
    ///                    MP4 container caveat: text-based subtitles (SRT, ASS) are converted
    ///                    to TX3G and embed fine. Image-based subtitles (PGS from Blu-rays,
    ///                    VOBSUB from DVDs) cannot be stored as selectable tracks in MP4 and
    ///                    will be silently dropped by HandBrake. If sources are Blu-ray rips
    ///                    with PGS subs, consider MKV output or burning them in instead.
    /// </summary>
    private static string BuildHandBrakeArgs(
        string inputPath, string outputPath, HandbrakeScriptOptions options)
    {
        var parts = new List<string>
        {
            "-i", Quote(inputPath),
            "-o", Quote(outputPath),
            "--format",          "av_mp4",
            "--encoder",         "x265",
            "--quality",         options.Quality.ToString(),
            "--encoder-preset",  options.Preset,
            // Audio: copy the most common lossy formats; fall back to AAC for anything else
            "--audio-copy-mask", "aac,ac3,eac3,truehd,dtshd,dts,mp3",
            "--audio-fallback",  "av_aac",
            "--aencoder",        "copy",
            // Subtitles: pass through all tracks as selectable options (not burned in)
            "--all-subtitles",
            // Include chapter markers if present in the source
            "--markers"
        };

        return string.Join(" ", parts);
    }

    // ─── Utilities ────────────────────────────────────────────────────────────

    /// <summary>
    /// Emits a StepFileProgress snapshot to the caller.
    /// The caller (PipelineCommandHandler) writes it into the run manifest so the
    /// mt-dashboard can render a live progress bar.
    /// </summary>
    private static void ReportProgress(
        Action<StepFileProgress>? onProgress,
        List<FileJobRecord>       jobs,
        string?                   currentFile)
    {
        if (onProgress is null) return;

        // Single pass — avoids iterating the job list twice per progress update.
        var processed = 0;
        var failed    = 0;
        foreach (var j in jobs)
        {
            if (j.Status is StepStatus.Complete or StepStatus.Failed) processed++;
            if (j.Status == StepStatus.Failed) failed++;
        }

        onProgress(new StepFileProgress
        {
            TotalFiles     = jobs.Count,
            ProcessedFiles = processed,
            FailedFiles    = failed,
            CurrentFile    = currentFile,
            // Include the full file list so the dashboard can render per-file indicators.
            // For very large sets this could be trimmed, but typical media batches are
            // small enough that the full list is fine.
            Files          = jobs.AsReadOnly()
        });
    }

    /// <summary>
    /// Tries each known HandBrakeCLI path and returns the first one that exists.
    /// Also attempts to resolve via PATH by checking 'which'/'where'.
    /// </summary>
    private static string? FindHandBrakeCLI()
    {
        foreach (var path in CandidatePaths)
            if (File.Exists(path))
                return path;

        // Fall back to PATH resolution
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir, "HandBrakeCLI");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Best-effort cleanup of a partially-written output file after encode failure.
    /// Leaves a warning in the log if deletion fails but never throws.
    /// </summary>
    private void TryDeletePartialOutput(string outputPath)
    {
        if (!File.Exists(outputPath)) return;
        try
        {
            File.Delete(outputPath);
            log.Warn($"[handbrake]   Deleted partial output: {outputPath}");
        }
        catch (Exception ex)
        {
            log.Warn($"[handbrake]   Could not delete partial output {outputPath}: {ex.Message}");
        }
    }

    private static string Quote(string path) => $"\"{path}\"";
}
