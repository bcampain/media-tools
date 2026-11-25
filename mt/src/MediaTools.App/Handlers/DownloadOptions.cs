namespace MediaTools.App.Handlers;

/// <summary>
/// CLI-level options for the `mt download` command.
///
/// This record is a direct projection from System.CommandLine's parse result,
/// so it carries the full common-options surface (DryRun, Yes, LogDir, …) even
/// though most of them aren't used by the download step today.  Having them here
/// keeps the pattern consistent with the other handlers and makes it easy to add
/// logging or verbosity behaviour later without touching the command definition.
/// </summary>
public record DownloadOptions(
    /// <summary>The URL to download (required positional argument).</summary>
    string Url,

    /// <summary>
    /// Destination directory for the downloaded file.
    /// Passed directly to yt-dlp's output template as the parent directory.
    /// </summary>
    string OutputDirectory,

    /// <summary>
    /// Container format for the final output file (default "mp4").
    /// Maps to yt-dlp's --merge-output-format flag.
    /// </summary>
    string MergeOutputFormat,

    bool   DryRun,
    bool   Yes,
    string LogDir
);
