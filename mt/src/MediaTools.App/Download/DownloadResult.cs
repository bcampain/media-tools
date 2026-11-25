namespace MediaTools.App.Download;

/// <summary>
/// Return value from IDownloadRunner.RunAsync.
///
/// Carrying the downloaded file paths here (not just an exit code) is a
/// deliberate design choice: when this download step is eventually integrated
/// into the pipeline, PipelineCommandHandler will be able to read
/// DownloadedFiles and pass them directly as the "target" for the handbrake
/// encoding step — no directory scan required.
///
/// Pattern: the other runners return plain int exit codes because they operate
/// on directories that already exist on disk. Download is different because the
/// output directory may be empty before the run, so explicit path tracking is
/// more reliable than a diff of the directory contents.
/// </summary>
public record DownloadResult(
    /// <summary>
    /// yt-dlp process exit code.  0 = success, non-zero = failure.
    /// </summary>
    int ExitCode,

    /// <summary>
    /// Absolute paths of every file written to disk by this invocation.
    /// Populated by parsing yt-dlp's stdout/stderr for:
    ///   [download] Destination: &lt;path&gt;           (no merge needed)
    ///   [Merger] Merging formats into "&lt;path&gt;"   (audio+video merge)
    ///
    /// Empty on dry-run or failure.
    /// </summary>
    IReadOnlyList<string> DownloadedFiles
);
