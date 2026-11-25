namespace MediaTools.App.Download;

/// <summary>
/// Configuration for a single yt-dlp download invocation.
///
/// These are the runner-level knobs — they map directly onto yt-dlp flags.
/// The CLI-level DownloadOptions record in MediaTools.App.Handlers carries the
/// same data plus the common options (DryRun, Yes, LogDir, …); the handler
/// projects it into this record before calling the runner so the runner stays
/// decoupled from System.CommandLine.
/// </summary>
public record DownloadRunnerOptions(
    /// <summary>
    /// Absolute path to the directory where the downloaded file should be saved.
    /// Maps to yt-dlp's -o / --output template: "{OutputDirectory}/%(title)s.%(ext)s"
    /// </summary>
    string OutputDirectory,

    /// <summary>
    /// Container format for the final output file.
    /// Maps to yt-dlp's --merge-output-format flag.
    /// Default: "mp4" — keeps outputs consistent with the rest of the pipeline.
    /// </summary>
    string MergeOutputFormat,

    /// <summary>
    /// When true the runner logs what it would do but does not launch yt-dlp.
    /// </summary>
    bool DryRun
);
