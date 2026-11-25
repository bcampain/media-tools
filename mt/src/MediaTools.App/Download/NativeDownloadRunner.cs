using System.Diagnostics;
using System.Text.RegularExpressions;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.App.Download;

/// <summary>
/// Native C# implementation of the yt-dlp download step.
///
/// Design notes
/// ────────────
/// • Binary resolution follows the same Lazy&lt;string?&gt; + candidate-path pattern
///   used by NativeNormalizeRunner for ffmpeg.  Candidate paths are checked first
///   (fast, no PATH traversal), then PATH is searched as a fallback.
///
/// • yt-dlp output parsing:
///   yt-dlp writes human-readable progress lines to stdout. Two line shapes are
///   relevant for knowing what files were written to disk:
///
///     [download] Destination: /abs/path/to/file.ext
///         → emitted when the file can be downloaded directly (no merge).
///
///     [Merger] Merging formats into "/abs/path/to/file.ext"
///         → emitted when separate audio + video streams are merged (common on
///           YouTube).  This is the final file — the separate stream files are
///           deleted by yt-dlp automatically after the merge.
///
///   We collect both, de-duplicate, and return them in DownloadResult.DownloadedFiles.
///   On a typical YouTube download only the Merger line will appear; on a direct
///   download (e.g. a plain MP4 on Twitter/X) only Destination will appear.
///
/// • Output template:
///   "-o {outputDir}/%(title)s.%(ext)s" tells yt-dlp to write into the caller-
///   supplied directory using the video title as the filename.  This is consistent
///   with the manual workflow documented in download-youtube-videos.md.
///
/// • Dry-run:
///   Passes --simulate to yt-dlp so it prints what it would do without downloading.
///   DownloadedFiles will be empty (nothing actually written).
/// </summary>
public class NativeDownloadRunner : IDownloadRunner
{
    // Ordered candidate paths checked before falling back to PATH.
    // The Docker image installs yt-dlp to /usr/local/bin; Homebrew (macOS dev)
    // uses /opt/homebrew/bin.
    private static readonly string[] YtDlpCandidatePaths =
    [
        "/usr/local/bin/yt-dlp",
        "/usr/bin/yt-dlp",
        "/opt/homebrew/bin/yt-dlp",
    ];

    // Lazy so the binary search runs once per process lifetime, not once per call.
    private readonly Lazy<string?> _ytDlpPath = new(FindYtDlp);

    // ── Output-line parsers ───────────────────────────────────────────────────

    // Matches:  [download] Destination: /path/to/file.webm
    // Group 1 captures the absolute path.
    private static readonly Regex DestinationRegex =
        new(@"^\[download\] Destination: (.+)$", RegexOptions.Compiled);

    // Matches:  [Merger] Merging formats into "/path/to/file.mp4"
    // Group 1 captures the path (without the surrounding quotes).
    private static readonly Regex MergerRegex =
        new(@"^\[Merger\] Merging formats into ""(.+)""$", RegexOptions.Compiled);

    // ── IDownloadRunner ───────────────────────────────────────────────────────

    public async Task<DownloadResult> RunAsync(
        string               url,
        DownloadRunnerOptions options,
        ILogSink             log,
        CancellationToken    ct)
    {
        var ytDlpPath = _ytDlpPath.Value;

        if (ytDlpPath is null)
        {
            log.Error("[download] yt-dlp not found.");
            log.Error("[download] Checked: " + string.Join(", ", YtDlpCandidatePaths));
            log.Error("[download] Install yt-dlp (https://github.com/yt-dlp/yt-dlp) and ensure it is on PATH.");
            return new DownloadResult(ExitCode: 1, DownloadedFiles: []);
        }

        log.Info($"[download] Using yt-dlp: {ytDlpPath}");

        // Ensure the output directory exists before yt-dlp tries to write there.
        // yt-dlp will not create intermediate directories on all platforms.
        try
        {
            Directory.CreateDirectory(options.OutputDirectory);
        }
        catch (Exception ex)
        {
            log.Error($"[download] Could not create output directory '{options.OutputDirectory}': {ex.Message}");
            return new DownloadResult(ExitCode: 1, DownloadedFiles: []);
        }

        // ── Build the yt-dlp argument list ────────────────────────────────────
        //
        // --merge-output-format mp4
        //     After downloading separate audio/video streams, merge them into a
        //     single MP4 container.  Requires ffmpeg (already installed in the image).
        //
        // -o "%(title)s.%(ext)s"
        //     Output filename template.  We set the working directory to OutputDirectory
        //     so relative paths resolve there.
        //
        // --no-simulate / --simulate
        //     --simulate: print what would be done, skip actual download.
        //     Omitted in real runs (yt-dlp downloads by default).

        var outputTemplate = Path.Combine(options.OutputDirectory, "%(title)s.%(ext)s");
        var args = options.DryRun
            ? $"--simulate --merge-output-format {options.MergeOutputFormat} -o \"{outputTemplate}\" \"{url}\""
            : $"--merge-output-format {options.MergeOutputFormat} -o \"{outputTemplate}\" \"{url}\"";

        log.Info($"[download] cmd: {ytDlpPath} {args}");

        if (options.DryRun)
        {
            log.Info("[download] DRYRUN — yt-dlp will simulate without downloading.");
        }

        // ── Launch process ────────────────────────────────────────────────────

        var psi = new ProcessStartInfo(ytDlpPath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            // Working directory does not affect our absolute -o template but is
            // set to OutputDirectory for any relative paths yt-dlp may resolve.
            WorkingDirectory = options.OutputDirectory
        };

        using var proc = new Process { StartInfo = psi };

        // Collect paths from output lines, kept in two separate sets because they
        // mean different things:
        //   destinationPaths — every file yt-dlp wrote, INCLUDING intermediate
        //                      stream parts (e.g. "Title.f137.mp4", "Title.f140.m4a")
        //                      that it deletes after merging.
        //   mergerPaths      — final merged outputs only.
        // Classifying by the *line type* (not by file extension) is what makes this
        // correct: an intermediate video part can have the same extension as the
        // final file, so an extension check can't tell them apart.
        // HashSet de-duplicates in case yt-dlp repeats a line.
        var destinationPaths = new HashSet<string>(StringComparer.Ordinal);
        var mergerPaths      = new HashSet<string>(StringComparer.Ordinal);

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;

            // Echo to console so the operator sees yt-dlp's progress in real time.
            Console.WriteLine(e.Data);

            // Parse for file paths we need to track.
            var destMatch   = DestinationRegex.Match(e.Data);
            var mergerMatch = MergerRegex.Match(e.Data);

            if (destMatch.Success)
                destinationPaths.Add(destMatch.Groups[1].Value.Trim());
            else if (mergerMatch.Success)
                mergerPaths.Add(mergerMatch.Groups[1].Value.Trim());
        };

        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) Console.Error.WriteLine(e.Data);
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try { await proc.WaitForExitAsync(ct); }
        catch (OperationCanceledException)
        {
            proc.Kill(entireProcessTree: true);
            throw;
        }

        // WaitForExit() (no timeout) ensures async output buffers are fully flushed
        // before we read the collected path sets — same pattern as NativeNormalizeRunner.
        proc.WaitForExit();

        var exitCode = proc.ExitCode;

        if (exitCode == 0)
        {
            if (options.DryRun)
            {
                log.Info("[download] DRYRUN complete — no files written.");
            }
            else
            {
                // If the merger ran, its outputs are the final files and the
                // Destination paths are deleted intermediates — so use only the
                // merger outputs. If no merge happened (single-stream download),
                // the Destination paths ARE the final files.
                var finalPaths = mergerPaths.Count > 0
                    ? mergerPaths.ToList()
                    : destinationPaths.ToList();
                finalPaths.Sort(StringComparer.Ordinal);

                log.Info($"[download] ✓ Downloaded {finalPaths.Count} file(s):");
                foreach (var f in finalPaths)
                    log.Info($"[download]   {f}");

                return new DownloadResult(ExitCode: 0, DownloadedFiles: finalPaths);
            }
        }
        else
        {
            log.Error($"[download] ✗ yt-dlp exited with code {exitCode}.");
        }

        return new DownloadResult(ExitCode: exitCode, DownloadedFiles: []);
    }

    // ── Binary resolution ─────────────────────────────────────────────────────

    private static string? FindYtDlp() => FindExecutable("yt-dlp", YtDlpCandidatePaths);

    /// <summary>
    /// Tries each known candidate path, then falls back to PATH resolution.
    /// Mirrors the strategy used by NativeNormalizeRunner for ffmpeg/ffprobe.
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
}
