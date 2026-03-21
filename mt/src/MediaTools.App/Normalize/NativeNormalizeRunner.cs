using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MediaTools.Domain.Models;
using MediaTools.Infrastructure.Logging;
using MediaTools.Infrastructure.Notifications;
using MediaTools.Scripts;

namespace MediaTools.App.Normalize;

/// <summary>
/// Native C# implementation of the audio normalization step.
/// Replaces the old NormalizeRunner that shelled out to /usr/local/bin/normalize_audio.
///
/// Responsibilities:
///   1. Discover .norm.mp4 files under the staging target path.
///   2. For each file: probe channel count, measure loudness (two-pass), apply normalization.
///   3. On success: rename the .tmp output over the final .mp4 path, then delete the .norm.mp4 source.
///   4. Report per-file progress via the onProgress callback so PipelineCommandHandler
///      can update the manifest and mt-dashboard can render a live progress bar.
///
/// Audio normalization targets (EBU R128):
///   - Integrated loudness (I):  options.TargetI  (default -16 LUFS in PipelineCommandHandler)
///   - True peak ceiling (TP):   options.TruePeak (default -1.5 dBTP)
///   - Loudness range (LRA):     options.Lra      (default 11 LU)
///
/// Surround (≥ 6 channels) with stereo track enabled (options.StereoTrack == "on"):
///   Track 0: normalized 5.1 surround at 512k AAC
///   Track 1: normalized stereo downmix at 256k AAC
///   Downmix:  pan=stereo|c0=0.5*FL+0.707*FC+0.5*BL|c1=0.5*FR+0.707*FC+0.5*BR
///
/// Stereo-only (< 6 channels, or stereo track disabled):
///   Single audio track at 256k AAC
///
/// Two-pass (default):
///   Pass 1 — measure: runs ffmpeg with loudnorm+print_format=json, extracts JSON from stderr.
///   Pass 2 — apply:   runs ffmpeg with linear loudnorm seeded from pass 1 measurements.
///   Silent audio guard: if input_i is -inf / null / out-of-range the file is renamed as-is
///   rather than normalized (prevents ffmpeg from producing garbage for silent content).
///
/// One-pass (options.OnePass == true):
///   Skips measurement; applies loudnorm directly. Less accurate but faster.
///   Silent audio guard is not applied in this mode (mirrors the bash script).
///
/// Temporary output:
///   ffmpeg writes to a .mp4.tmp.mp4 sibling, which is atomically moved over the .mp4
///   destination only after a successful encode. This prevents partial files from ever
///   appearing at the final path.
/// </summary>
public class NativeNormalizeRunner(IDiscordNotifier discord) : INormalizeRunner
{
    // Ordered lists of candidate paths checked at startup.
    private static readonly string[] FfmpegCandidatePaths =
    [
        "/usr/local/bin/ffmpeg",
        "/usr/bin/ffmpeg",
        "/opt/homebrew/bin/ffmpeg",
    ];

    private static readonly string[] FfprobeCandidatePaths =
    [
        "/usr/local/bin/ffprobe",
        "/usr/bin/ffprobe",
        "/opt/homebrew/bin/ffprobe",
    ];

    // Lazy so the PATH search runs once per process lifetime, not once per file.
    private readonly Lazy<string?> _ffmpegPath  = new(FindFfmpeg);
    private readonly Lazy<string?> _ffprobePath = new(FindFfprobe);

    // The stereo downmix pan matrix — matches the bash script exactly.
    // Each output channel is a weighted sum of the 5.1 input channels:
    //   c0 (L) = 0.5*FL + 0.707*FC + 0.5*BL
    //   c1 (R) = 0.5*FR + 0.707*FC + 0.5*BR
    // FC gets a slightly higher weight (0.707 ≈ -3dB) because the centre channel
    // is folded into both left and right, so it needs to be attenuated to avoid clipping.
    private const string StereoPanFilter =
        "pan=stereo|c0=0.5*FL+0.707*FC+0.5*BL|c1=0.5*FR+0.707*FC+0.5*BR";

    // ─────────────────────────────────────────────────────────────────────────

    public async Task<int> RunAsync(
        string                    target,
        PipelineRun               run,
        NormalizeScriptOptions    options,
        Action<StepFileProgress>? onProgress,
        ILogSink                  log,
        CancellationToken         ct,
        IReadOnlySet<string>?     inheritedFiles = null)
    {
        var ffmpegPath  = _ffmpegPath.Value;
        var ffprobePath = _ffprobePath.Value;

        if (ffmpegPath is null)
        {
            log.Error("[normalize] ffmpeg not found.");
            log.Error("[normalize] Checked: " + string.Join(", ", FfmpegCandidatePaths));
            log.Error("[normalize] Install ffmpeg and ensure it is on PATH.");
            return 1;
        }

        if (ffprobePath is null)
        {
            log.Error("[normalize] ffprobe not found.");
            log.Error("[normalize] Checked: " + string.Join(", ", FfprobeCandidatePaths));
            log.Error("[normalize] Install ffprobe (ships alongside ffmpeg).");
            return 1;
        }

        log.Info($"[normalize] Using ffmpeg:  {ffmpegPath}");
        log.Info($"[normalize] Using ffprobe: {ffprobePath}");
        log.Info($"[normalize] Settings: I={options.TargetI} TP={options.TruePeak} LRA={options.Lra} " +
                 $"stereo_track={options.StereoTrack} one_pass={options.OnePass} " +
                 $"dry_run={options.DryRun} force={options.Force}");

        // ── Discover .norm.mp4 input files ───────────────────────────────────
        var inputFiles = File.Exists(target)
            ? ScanSingleFile(target)
            : ScanDirectory(target);

        if (inputFiles.Count == 0)
        {
            log.Warn($"[normalize] No .norm.mp4 files found under: {target}");
            log.Warn("[normalize] Nothing to do. Exiting with success.");
            await discord.NotifyAsync(
                "⚠️ Step 2 of 3: Normalize Audio finished",
                $"Target: {target} | run_id={run.RunId}\nNo .norm.mp4 files found, nothing to do.",
                run.LogFile, ct);
            return 0;
        }

        log.Info($"[normalize] Found {inputFiles.Count} file(s) to normalize.");

        // ── Build the job list ────────────────────────────────────────────────
        // A completed normalize job deletes its .norm.mp4, so inherited files
        // from a prior run won't appear in the discovery scan.  Union them back
        // in and sort the combined set before projecting to job records.
        var allPaths = inheritedFiles is null
            ? inputFiles
            : inputFiles
                .Concat(inheritedFiles.Except(inputFiles, StringComparer.Ordinal))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

        var jobs = allPaths
            .Select(f => new FileJobRecord
            {
                InputPath  = f,
                OutputPath = NormMp4ToMp4(f),
                Status     = inheritedFiles?.Contains(f) == true
                                 ? StepStatus.Inherited
                                 : StepStatus.Pending
            })
            .ToList();

        var inheritedCount = jobs.Count(j => j.Status == StepStatus.Inherited);
        if (inheritedCount > 0)
            log.Info($"[normalize] {inheritedCount} file(s) inherited from prior run — skipping re-normalize.");

        // Emit initial progress so the dashboard shows the total file count immediately.
        ReportProgress(onProgress, jobs, currentFile: null);

        await discord.NotifyAsync(
            "🔉 Step 2 of 3: Normalize Audio started",
            $"Target: {target} | run_id={run.RunId}",
            null, ct);

        var failedCount  = 0;
        var skippedCount = 0;
        var createdCount = 0;

        for (var i = 0; i < jobs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var job      = jobs[i];
            var fileName = Path.GetFileName(job.InputPath);

            // Skip files inherited from a prior run
            if (job.Status == StepStatus.Inherited)
            {
                log.Info($"[normalize] [{i + 1}/{jobs.Count}] {fileName} (inherited — skipped)");
                continue;
            }

            log.Info($"[normalize] [{i + 1}/{jobs.Count}] {fileName}");
            log.Info($"[normalize]   input:  {job.InputPath}");
            log.Info($"[normalize]   output: {job.OutputPath}");

            // Mark this file as running so the dashboard shows it in-progress.
            jobs[i] = job with { Status = StepStatus.Running, StartedAt = DateTime.UtcNow };
            ReportProgress(onProgress, jobs, currentFile: fileName);

            // ── Skip if output already exists and --force not requested ───────
            if (!options.Force && File.Exists(job.OutputPath))
            {
                log.Info($"[normalize]   Skipping — output already exists (use --force to re-normalize).");
                jobs[i] = jobs[i] with { Status = StepStatus.Skipped, CompletedAt = DateTime.UtcNow, ExitCode = 0 };
                ReportProgress(onProgress, jobs, currentFile: null);
                skippedCount++;
                continue;
            }

            // ── Dry-run: log only, do not encode ─────────────────────────────
            if (options.DryRun)
            {
                log.Info($"[normalize]   DRYRUN: would normalize {fileName} → {Path.GetFileName(job.OutputPath)}");
                jobs[i] = jobs[i] with { Status = StepStatus.Skipped, CompletedAt = DateTime.UtcNow, ExitCode = 0 };
                ReportProgress(onProgress, jobs, currentFile: null);
                skippedCount++;
                continue;
            }

            // ── Probe channel count ───────────────────────────────────────────
            var channels = await ProbeChannelCountAsync(ffprobePath, job.InputPath, ct);
            if (channels is null)
            {
                log.Error($"[normalize]   FAILED (no audio info): {fileName}");
                jobs[i] = jobs[i] with { Status = StepStatus.Failed, CompletedAt = DateTime.UtcNow, ExitCode = 1 };
                ReportProgress(onProgress, jobs, currentFile: null);
                await discord.NotifyAsync(
                    $"❌ ffmpeg: Failed to normalize audio ({i + 1}/{jobs.Count})",
                    $"Target: {target} | run_id={run.RunId}\nFailed file: {fileName}\n" +
                    $"Reason: could not probe audio channel count",
                    run.LogFile, ct);
                failedCount++;
                continue;
            }

            log.Info($"[normalize]   channels: {channels}");

            // ── Build and run the ffmpeg command ──────────────────────────────
            var addStereo = channels >= 6 &&
                            string.Equals(options.StereoTrack, "on", StringComparison.OrdinalIgnoreCase);
            var tmpFile   = BuildTmpPath(job.OutputPath);
            int rc;

            if (options.OnePass)
            {
                rc = await ApplyOnePassAsync(ffmpegPath, job.InputPath, tmpFile, options, addStereo, log, ct);
            }
            else
            {
                // Two-pass: measure (pass 1) then apply with measurements (pass 2).
                var measure = await MeasureAsync(ffmpegPath, job.InputPath, options, addStereo, log, ct);

                if (measure.IsSilent)
                {
                    // Silent/near-silent audio: ffmpeg cannot produce meaningful loudnorm
                    // output. Rename the .norm.mp4 directly to .mp4 without processing.
                    // This mirrors the bash script's behaviour of mv "$in_file" "$out_file".
                    log.Warn($"[normalize]   Silent audio detected — renaming as-is without normalization.");
                    try
                    {
                        File.Move(job.InputPath, job.OutputPath, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        log.Error($"[normalize]   Failed to rename silent file: {ex.Message}");
                        jobs[i] = jobs[i] with { Status = StepStatus.Failed, CompletedAt = DateTime.UtcNow, ExitCode = 1 };
                        ReportProgress(onProgress, jobs, currentFile: null);
                        await discord.NotifyAsync(
                            $"❌ ffmpeg: Failed to handle silent audio ({i + 1}/{jobs.Count})",
                            $"Target: {target} | run_id={run.RunId}\nFailed file: {fileName}",
                            run.LogFile, ct);
                        failedCount++;
                        continue;
                    }

                    log.Info($"[normalize]   WARN (silent audio, skipped normalization) [{i + 1}/{jobs.Count}]: " +
                             $"{fileName} → {Path.GetFileName(job.OutputPath)}");
                    jobs[i] = jobs[i] with { Status = StepStatus.Skipped, CompletedAt = DateTime.UtcNow, ExitCode = 0 };
                    ReportProgress(onProgress, jobs, currentFile: null);
                    skippedCount++;
                    await discord.NotifyAsync(
                        $"⚠️ ffmpeg: Silent audio, skipped normalization ({i + 1}/{jobs.Count})",
                        $"WARN silent audio (skipped normalization): {Path.GetFileName(job.OutputPath)}",
                        null, ct);
                    continue;
                }

                // Log the measured loudness values (mirrors bash log_line output).
                if (addStereo)
                {
                    log.Info($"[normalize]   measure: {fileName}");
                    log.Info($"[normalize]     5.1 input_i={measure.Surround!.InputI} " +
                             $"input_tp={measure.Surround.InputTp} " +
                             $"input_lra={measure.Surround.InputLra} " +
                             $"offset={measure.Surround.TargetOffset}");
                    log.Info($"[normalize]     st  input_i={measure.Stereo!.InputI} " +
                             $"input_tp={measure.Stereo.InputTp} " +
                             $"input_lra={measure.Stereo.InputLra} " +
                             $"offset={measure.Stereo.TargetOffset}");
                }
                else
                {
                    log.Info($"[normalize]   measure: {fileName} " +
                             $"input_i={measure.Stereo!.InputI} " +
                             $"input_tp={measure.Stereo.InputTp} " +
                             $"input_lra={measure.Stereo.InputLra} " +
                             $"offset={measure.Stereo.TargetOffset}");
                }

                    rc = await ApplyTwoPassAsync(ffmpegPath, job.InputPath, tmpFile, options, addStereo, measure, log, ct);
            }

            var completedAt = DateTime.UtcNow;
            if (rc == 0)
            {
                // Atomically swap the temp file into the final position, then remove
                // the .norm.mp4 source so downstream steps see only plain .mp4 files.
                try
                {
                    File.Move(tmpFile, job.OutputPath, overwrite: true);
                    File.Delete(job.InputPath);
                }
                catch (Exception ex)
                {
                    log.Error($"[normalize]   Failed to finalize output: {ex.Message}");
                    TryDeleteTemp(tmpFile, log);
                    jobs[i] = jobs[i] with { Status = StepStatus.Failed, CompletedAt = completedAt, ExitCode = 1 };
                    ReportProgress(onProgress, jobs, currentFile: null);
                    failedCount++;
                    continue;
                }

                var passLabel = options.OnePass ? "1pass" : "2pass";
                log.Info($"[normalize]   ✓ NORMALIZED({passLabel}) + DELETED .norm [{i + 1}/{jobs.Count}]: " +
                         $"{fileName} → {Path.GetFileName(job.OutputPath)}");

                // Update status and persist before notifying Discord so state is
                // accurate even if the notification fails.
                jobs[i] = jobs[i] with { Status = StepStatus.Complete, CompletedAt = completedAt, ExitCode = 0 };
                createdCount++;
                ReportProgress(onProgress, jobs, currentFile: null);

                await discord.NotifyAsync(
                    $"☑️ ffmpeg: Normalized audio ({i + 1}/{jobs.Count})",
                    $"NORMALIZED({passLabel}): {Path.GetFileName(job.OutputPath)}\nDELETED: {fileName}",
                    null, ct);
            }
            else
            {
                log.Error($"[normalize]   ✗ FAILED (exit {rc}): {fileName}");
                TryDeleteTemp(tmpFile, log);

                // Same reason — persist state before Discord.
                jobs[i] = jobs[i] with { Status = StepStatus.Failed, CompletedAt = completedAt, ExitCode = rc };
                failedCount++;
                ReportProgress(onProgress, jobs, currentFile: null);

                await discord.NotifyAsync(
                    $"❌ ffmpeg: Failed to normalize audio ({i + 1}/{jobs.Count})",
                    $"Target: {target} | run_id={run.RunId}\nFailed file: {fileName}",
                    run.LogFile, ct);
            }
        }

        log.Info($"[normalize] Complete: {createdCount + skippedCount}/{jobs.Count - inheritedCount} succeeded" +
                 (failedCount > 0  ? $", {failedCount} failed"   : "") +
                 (inheritedCount > 0 ? $", {inheritedCount} inherited" : ""));

        var completeMessage = $"Target: {target} | run_id={run.RunId} | " +
                              $"normalized={createdCount} skipped={skippedCount} failed={failedCount}";
        await discord.NotifyAsync("✅ Step 2 of 3: Normalize Audio finished", completeMessage, run.LogFile, ct);

        // Exit 3 on partial failure to match the bash script's convention (exit 3 when FAILS > 0).
        // This distinguishes "some files failed" (3) from "tool not found" (1).
        return failedCount > 0 ? 3 : 0;
    }

    // ─── File Discovery ───────────────────────────────────────────────────────

    /// <summary>
    /// Finds all .norm.mp4 files recursively under <paramref name="rootPath"/>.
    /// Skips hidden macOS metadata files (._* prefix and .DS_Store) to match
    /// the bash script's find … ! -name "._*" ! -name ".DS_Store" filter.
    /// Results are sorted by full path for deterministic processing order.
    /// </summary>
    private static List<string> ScanDirectory(string rootPath)
    {
        if (!Directory.Exists(rootPath)) return [];

        return Directory
            .EnumerateFiles(rootPath, "*.norm.mp4", SearchOption.AllDirectories)
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                return !name.StartsWith("._") && name != ".DS_Store";
            })
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Validates and returns a single .norm.mp4 file target.
    /// Returns an empty list if the file does not exist or is not a .norm.mp4.
    /// </summary>
    private static List<string> ScanSingleFile(string filePath)
    {
        if (!File.Exists(filePath)) return [];
        if (!filePath.EndsWith(".norm.mp4", StringComparison.OrdinalIgnoreCase)) return [];
        return [filePath];
    }

    // ─── Path Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a .norm.mp4 staging file to its final .mp4 output path.
    /// Strips the ".norm.mp4" suffix and appends ".mp4".
    ///
    /// Example: /staging/tv/ShowName/S01E01.norm.mp4 → /staging/tv/ShowName/S01E01.mp4
    ///
    /// This is the inverse of VideoPathMapper.MapHandbrakeOutput and completes the
    /// handoff contract: step 1 produces .norm.mp4, step 2 produces .mp4.
    /// </summary>
    private static string NormMp4ToMp4(string normMp4Path)
    {
        const string suffix = ".norm.mp4";
        return normMp4Path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? normMp4Path[..^suffix.Length] + ".mp4"
            : normMp4Path + ".mp4";
    }

    /// <summary>
    /// Builds the temporary output path written during ffmpeg encoding.
    /// The temp file is a sibling of the final output, distinguishable by its extension.
    ///
    /// Example: /staging/movies/Alien.mp4 → /staging/movies/Alien.mp4.tmp.mp4
    ///
    /// Mirrors the bash script's pattern: tmp_file="${out_file%.mp4}.mp4.tmp.mp4"
    /// Using .tmp.mp4 (not just .tmp) keeps ffmpeg happy with the container format.
    /// </summary>
    private static string BuildTmpPath(string outPath)
    {
        var dir  = Path.GetDirectoryName(outPath)!;
        var stem = Path.GetFileNameWithoutExtension(outPath);   // e.g. "Alien"
        return Path.Combine(dir, stem + ".mp4.tmp.mp4");        // e.g. "Alien.mp4.tmp.mp4"
    }

    // ─── Audio Channel Probing ────────────────────────────────────────────────

    /// <summary>
    /// Probes the channel count of the first audio stream via ffprobe.
    /// Returns null if ffprobe fails or no audio stream is found.
    ///
    /// Mirrors the bash ffprobe_channels() function:
    ///   ffprobe -v error -select_streams a:0 -show_entries stream=channels \
    ///           -of default=nw=1:nk=1 "$in_file"
    /// </summary>
    private async Task<int?> ProbeChannelCountAsync(
        string            ffprobePath,
        string            inputFile,
        CancellationToken ct)
    {
        var args = $"-v error -select_streams a:0 -show_entries stream=channels " +
                   $"-of default=nw=1:nk=1 {Q(inputFile)}";

        var psi = new ProcessStartInfo(ffprobePath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            WorkingDirectory       = "/"
        };

        using var proc = new Process { StartInfo = psi };
        var outputBuf = new StringBuilder();

        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) outputBuf.AppendLine(e.Data); };
        // Suppress ffprobe stderr (the -v error flag already limits it to real errors).
        proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try { await proc.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { proc.Kill(entireProcessTree: true); throw; }
        proc.WaitForExit();

        var raw = outputBuf.ToString().Trim();
        return int.TryParse(raw, out var ch) ? ch : (int?)null;
    }

    // ─── Loudness Measurement (Two-Pass, Pass 1) ──────────────────────────────

    // Internal record types for structured measurement results.
    private sealed record LoudnormMeasurement(
        string InputI,
        string InputTp,
        string InputLra,
        string InputThresh,
        string TargetOffset);

    private sealed record MeasureResult(
        LoudnormMeasurement? Surround,   // non-null only in surround+stereo mode
        LoudnormMeasurement? Stereo,     // present in all two-pass paths
        bool                 IsSilent);

    /// <summary>
    /// Runs the loudnorm measurement pass (ffmpeg pass 1) for a file.
    ///
    /// In surround+stereo mode two separate measurements are taken:
    ///   • The raw 5.1 audio stream (no pre-filter)
    ///   • The stereo downmix (prefixed with the pan filter)
    /// This mirrors the bash script's dual measure_loudnorm() calls.
    ///
    /// Returns MeasureResult.IsSilent = true if either measurement is invalid
    /// (input_i is -inf, null, or outside [-99, 0]).
    /// </summary>
    private async Task<MeasureResult> MeasureAsync(
        string                 ffmpegPath,
        string                 inputFile,
        NormalizeScriptOptions options,
        bool                   addStereo,
        ILogSink               log,
        CancellationToken      ct)
    {
        if (addStereo)
        {
            log.Info("[normalize]   Measuring surround (5.1) loudness...");
            var meas51 = await MeasureSingleAsync(ffmpegPath, inputFile, options, preFilter: null, log, ct);

            log.Info("[normalize]   Measuring stereo downmix loudness...");
            var measSt = await MeasureSingleAsync(ffmpegPath, inputFile, options, preFilter: StereoPanFilter, log, ct);

            if (!IsLoudnormMeasValid(meas51) || !IsLoudnormMeasValid(measSt))
                return new MeasureResult(null, null, IsSilent: true);

            return new MeasureResult(Surround: meas51, Stereo: measSt, IsSilent: false);
        }
        else
        {
            log.Info("[normalize]   Measuring loudness...");
            var meas = await MeasureSingleAsync(ffmpegPath, inputFile, options, preFilter: null, log, ct);

            if (!IsLoudnormMeasValid(meas))
                return new MeasureResult(null, null, IsSilent: true);

            return new MeasureResult(Surround: null, Stereo: meas, IsSilent: false);
        }
    }

    /// <summary>
    /// Runs a single loudnorm measurement pass via ffmpeg.
    ///
    /// Mirrors the bash measure_loudnorm() function:
    ///   ffmpeg -hide_banner -i "$in_file" \
    ///     -map 0:a:0 -vn -sn -dn \
    ///     -af "$af" \
    ///     -f null - 2>&1 | tee /dev/stderr
    ///   extract_loudnorm_json (awk-based)
    ///
    /// The loudnorm filter writes its JSON measurement block to stderr.
    /// We capture stderr and extract the JSON block from it.
    ///
    /// <paramref name="preFilter"/> is prepended to the af chain before loudnorm,
    /// which is how the stereo downmix measurement works (pan filter applied first).
    /// </summary>
    private async Task<LoudnormMeasurement?> MeasureSingleAsync(
        string                 ffmpegPath,
        string                 inputFile,
        NormalizeScriptOptions options,
        string?                preFilter,
        ILogSink               log,
        CancellationToken      ct)
    {
        var loudnormFilter =
            $"loudnorm=I={options.TargetI}:TP={options.TruePeak}:LRA={options.Lra}:print_format=json";
        var af = preFilter is not null ? $"{preFilter},{loudnormFilter}" : loudnormFilter;

        var args = $"-hide_banner -i {Q(inputFile)} " +
                   $"-map 0:a:0 -vn -sn -dn " +
                   $"-af {Q(af)} " +
                   $"-f null -";

        var psi = new ProcessStartInfo(ffmpegPath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            WorkingDirectory       = "/"
        };

        using var proc = new Process { StartInfo = psi };
        var stderrBuf = new StringBuilder();

        // loudnorm prints its JSON block to stderr. Mirror it to the console so the
        // operator sees the measurement in real-time (matches bash tee /dev/stderr).
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
        proc.ErrorDataReceived  += (_, e) =>
        {
            if (e.Data is null) return;
            Console.Error.WriteLine(e.Data);
            stderrBuf.AppendLine(e.Data);
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try { await proc.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { proc.Kill(entireProcessTree: true); throw; }
        proc.WaitForExit();

        return ExtractLoudnormJson(stderrBuf.ToString(), log);
    }

    /// <summary>
    /// Extracts and parses the loudnorm JSON measurement block from ffmpeg's stderr output.
    /// Returns null if no valid JSON block is found or parsing fails.
    ///
    /// Mirrors the bash extract_loudnorm_json() awk script: finds the first '{',
    /// tracks brace depth, and captures everything up to the matching '}'.
    /// </summary>
    private static LoudnormMeasurement? ExtractLoudnormJson(string ffmpegOutput, ILogSink log)
    {
        var startIdx = ffmpegOutput.IndexOf('{');
        if (startIdx < 0) return null;

        var depth  = 0;
        var endIdx = -1;
        for (var i = startIdx; i < ffmpegOutput.Length; i++)
        {
            if      (ffmpegOutput[i] == '{') depth++;
            else if (ffmpegOutput[i] == '}') { depth--; }

            if (depth == 0) { endIdx = i; break; }
        }

        if (endIdx < 0) return null;

        var json = ffmpegOutput[startIdx..(endIdx + 1)];
        try
        {
            using var doc  = JsonDocument.Parse(json);
            var       root = doc.RootElement;

            return new LoudnormMeasurement(
                InputI:       root.GetProperty("input_i").GetString()       ?? "",
                InputTp:      root.GetProperty("input_tp").GetString()      ?? "",
                InputLra:     root.GetProperty("input_lra").GetString()     ?? "",
                InputThresh:  root.GetProperty("input_thresh").GetString()  ?? "",
                TargetOffset: root.GetProperty("target_offset").GetString() ?? "");
        }
        catch (Exception ex)
        {
            log.Warn($"[normalize]   Could not parse loudnorm JSON: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Returns true only if the measurement contains a meaningful input_i value.
    ///
    /// Mirrors the bash loudnorm_meas_valid() function:
    ///   - null / "-inf" / "inf" / empty → invalid (silent content)
    ///   - Must parse as a number in [-99, 0]
    ///
    /// The [-99, 0] range guard prevents treating erroneous measurements (e.g. +5 LUFS)
    /// as valid, which would produce incorrect normalization.
    /// </summary>
    private static bool IsLoudnormMeasValid(LoudnormMeasurement? meas)
    {
        if (meas is null) return false;
        var v = meas.InputI;
        if (string.IsNullOrEmpty(v) || v == "null" || v == "-inf" || v == "inf") return false;
        return double.TryParse(v, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out var d)
               && d >= -99.0 && d <= 0.0;
    }

    /// <summary>
    /// Constructs the loudnorm filter string for the apply pass (pass 2).
    ///
    /// Mirrors the bash apply_two_pass_filter() function:
    ///   printf "loudnorm=I=%s:TP=%s:LRA=%s:measured_I=%s:measured_TP=%s:
    ///           measured_LRA=%s:measured_thresh=%s:offset=%s:linear=true:print_format=summary"
    ///
    /// linear=true enables phase-accurate gain adjustment (the "true peak" approach),
    /// which is more accurate than one-pass dynamic normalization for small adjustments.
    /// print_format=summary emits a concise text summary rather than a JSON block.
    /// </summary>
    private static string BuildTwoPassFilter(LoudnormMeasurement meas, NormalizeScriptOptions options) =>
        $"loudnorm=I={options.TargetI}:TP={options.TruePeak}:LRA={options.Lra}" +
        $":measured_I={meas.InputI}:measured_TP={meas.InputTp}" +
        $":measured_LRA={meas.InputLra}:measured_thresh={meas.InputThresh}" +
        $":offset={meas.TargetOffset}:linear=true:print_format=summary";

    // ─── ffmpeg Application (Pass 2 / One-Pass) ───────────────────────────────

    /// <summary>
    /// One-pass normalization: applies loudnorm directly without prior measurement.
    /// Less loudness-accurate than two-pass (ffmpeg picks the algorithm dynamically),
    /// but significantly faster for large batches where approximate normalisation is acceptable.
    ///
    /// Surround+stereo ffmpeg mapping (mirrors bash):
    ///   -map 0:v:0 -map 0:a:0 -map 0:a:0 -map 0:s?
    ///   -c:v copy
    ///   -c:a:0 aac -b:a:0 512k   ← surround track
    ///   -c:a:1 aac -b:a:1 256k   ← stereo downmix
    ///   -c:s copy
    ///   -filter:a:0 loudnorm=…
    ///   -filter:a:1 pan=stereo…,loudnorm=…
    ///   -movflags +faststart
    ///
    /// Stereo-only mapping:
    ///   -map 0:v:0 -map 0:a:0 -map 0:s?
    ///   -c:v copy -c:a aac -b:a 256k -c:s copy
    ///   -filter:a loudnorm=…
    ///   -movflags +faststart
    /// </summary>
    private async Task<int> ApplyOnePassAsync(
        string                 ffmpegPath,
        string                 inputFile,
        string                 tmpFile,
        NormalizeScriptOptions options,
        bool                   addStereo,
        ILogSink               log,
        CancellationToken      ct)
    {
        var simpleFilter  = $"loudnorm=I={options.TargetI}:TP={options.TruePeak}:LRA={options.Lra}";
        var stereoFilter  = $"{StereoPanFilter},{simpleFilter}";

        string args;
        if (addStereo)
        {
            args = $"-hide_banner -y -i {Q(inputFile)} " +
                   $"-map 0:v:0 -map 0:a:0 -map 0:a:0 -map 0:s? " +
                   $"-c:v copy " +
                   $"-c:a:0 aac -b:a:0 512k " +
                   $"-c:a:1 aac -b:a:1 256k " +
                   $"-c:s copy " +
                   $"-filter:a:0 {Q(simpleFilter)} " +
                   $"-filter:a:1 {Q(stereoFilter)} " +
                   $"-movflags +faststart " +
                   Q(tmpFile);
        }
        else
        {
            args = $"-hide_banner -y -i {Q(inputFile)} " +
                   $"-map 0:v:0 -map 0:a:0 -map 0:s? " +
                   $"-c:v copy " +
                   $"-c:a aac -b:a 256k " +
                   $"-c:s copy " +
                   $"-filter:a {Q(simpleFilter)} " +
                   $"-movflags +faststart " +
                   Q(tmpFile);
        }

        return await RunFfmpegAsync(ffmpegPath, args, log, ct);
    }

    /// <summary>
    /// Two-pass normalization: applies loudnorm in linear mode seeded with the measurements
    /// from pass 1. This produces the most accurate result because ffmpeg knows the exact
    /// gain adjustment needed before it starts encoding.
    ///
    /// The filter strings are constructed by BuildTwoPassFilter(), which populates the
    /// measured_I / measured_TP / measured_LRA / measured_thresh / offset parameters.
    /// For surround+stereo, the stereo downmix filter chain is:
    ///   pan=stereo|…,loudnorm=I=…:measured_I=…:linear=true:…
    /// </summary>
    private async Task<int> ApplyTwoPassAsync(
        string                 ffmpegPath,
        string                 inputFile,
        string                 tmpFile,
        NormalizeScriptOptions options,
        bool                   addStereo,
        MeasureResult          measure,
        ILogSink               log,
        CancellationToken      ct)
    {
        string args;
        if (addStereo)
        {
            var f51 = BuildTwoPassFilter(measure.Surround!, options);
            var fst = $"{StereoPanFilter},{BuildTwoPassFilter(measure.Stereo!, options)}";

            args = $"-hide_banner -y -i {Q(inputFile)} " +
                   $"-map 0:v:0 -map 0:a:0 -map 0:a:0 -map 0:s? " +
                   $"-c:v copy " +
                   $"-c:a:0 aac -b:a:0 512k " +
                   $"-c:a:1 aac -b:a:1 256k " +
                   $"-c:s copy " +
                   $"-filter:a:0 {Q(f51)} " +
                   $"-filter:a:1 {Q(fst)} " +
                   $"-movflags +faststart " +
                   Q(tmpFile);
        }
        else
        {
            var f = BuildTwoPassFilter(measure.Stereo!, options);

            args = $"-hide_banner -y -i {Q(inputFile)} " +
                   $"-map 0:v:0 -map 0:a:0 -map 0:s? " +
                   $"-c:v copy " +
                   $"-c:a aac -b:a 256k " +
                   $"-c:s copy " +
                   $"-filter:a {Q(f)} " +
                   $"-movflags +faststart " +
                   Q(tmpFile);
        }

        return await RunFfmpegAsync(ffmpegPath, args, log, ct);
    }

    /// <summary>
    /// Launches an ffmpeg process, streams stdout and stderr to the console, and returns exit code.
    /// Both streams are forwarded verbatim — ffmpeg writes progress to stderr and it should
    /// be visible in the terminal so the operator can monitor encoding in real-time.
    /// </summary>
    private async Task<int> RunFfmpegAsync(string ffmpegPath, string args, ILogSink log, CancellationToken ct)
    {
        log.Info($"[normalize]   cmd: {ffmpegPath} {args}");

        var psi = new ProcessStartInfo(ffmpegPath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            WorkingDirectory       = "/"
        };

        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try { await proc.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { proc.Kill(entireProcessTree: true); throw; }

        // Flush any buffered output before returning, same as NativeHandbrakeRunner.
        proc.WaitForExit();
        return proc.ExitCode;
    }

    // ─── Progress Reporting ───────────────────────────────────────────────────

    /// <summary>
    /// Emits a StepFileProgress snapshot to the pipeline handler.
    /// The handler writes it into the run manifest so mt-dashboard can render
    /// a live progress bar ("4 of 12 files normalized").
    /// </summary>
    private static void ReportProgress(
        Action<StepFileProgress>? onProgress,
        List<FileJobRecord>       jobs,
        string?                   currentFile)
    {
        if (onProgress is null) return;

        var processed = 0;
        var failed    = 0;
        var skipped   = 0;
        var inherited = 0;
        foreach (var j in jobs)
        {
            if (j.Status is StepStatus.Complete or StepStatus.Failed or StepStatus.Skipped or StepStatus.Inherited) processed++;
            if (j.Status == StepStatus.Failed)    failed++;
            if (j.Status == StepStatus.Skipped)   skipped++;
            if (j.Status == StepStatus.Inherited) inherited++;
        }

        onProgress(new StepFileProgress
        {
            TotalFiles     = jobs.Count,
            ProcessedFiles = processed,
            FailedFiles    = failed,
            SkippedFiles   = skipped,
            InheritedFiles = inherited,
            CurrentFile    = currentFile,
            Files          = jobs.AsReadOnly()
        });
    }

    // ─── Utilities ────────────────────────────────────────────────────────────

    /// <summary>
    /// Best-effort cleanup of a partially-written temp file after encode failure.
    /// Logs a warning if deletion fails but never throws.
    /// </summary>
    private static void TryDeleteTemp(string tmpPath, ILogSink log)
    {
        if (!File.Exists(tmpPath)) return;
        try
        {
            File.Delete(tmpPath);
            log.Warn($"[normalize]   Deleted partial temp: {tmpPath}");
        }
        catch (Exception ex)
        {
            log.Warn($"[normalize]   Could not delete temp {tmpPath}: {ex.Message}");
        }
    }

    private static string? FindFfmpeg()  => FindExecutable("ffmpeg",  FfmpegCandidatePaths);
    private static string? FindFfprobe() => FindExecutable("ffprobe", FfprobeCandidatePaths);

    /// <summary>
    /// Tries each known candidate path, then falls back to PATH resolution.
    /// Mirrors the Lazy<string> strategy used by NativeHandbrakeRunner for HandBrakeCLI.
    /// </summary>
    private static string? FindExecutable(string name, string[] candidates)
    {
        foreach (var path in candidates)
            if (File.Exists(path))
                return path;

        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string Q(string s) => $"\"{s}\"";
}
