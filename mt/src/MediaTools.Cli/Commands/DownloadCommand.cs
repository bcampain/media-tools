using System.CommandLine;
using System.CommandLine.Invocation;
using MediaTools.App.Handlers;

namespace MediaTools.Cli.Commands;

/// <summary>
/// Defines the `mt download` command.
///
/// Usage:
///   mt download &lt;url&gt; --output-dir &lt;path&gt; [--format mp4] [--dry-run] [--yes]
///
/// Arguments:
///   url             URL of the YouTube / X.com video to download.
///
/// Options:
///   --output-dir    Directory where the downloaded file will be saved.
///                   yt-dlp will write  "{output-dir}/{video title}.{ext}".
///   --format        Container format for the merged output (default: mp4).
///                   Passed to yt-dlp's --merge-output-format flag.
///                   Requires ffmpeg for formats that need stream merging.
///   --dry-run       Simulate without downloading (passes --simulate to yt-dlp).
///   --yes           Skip the "Are you sure?" confirmation prompt.
///
/// Design note — why --output-dir is required (not defaulted):
///   Unlike the pipeline commands whose directories are well-known Docker volume
///   mount points (/incoming, /staging, /library), download has no natural home.
///   Requiring the caller to be explicit prevents accidental writes to unexpected
///   locations (e.g. the container root).  A later enhancement could add a
///   --downloads-root option backed by an env var if a default becomes useful.
/// </summary>
public static class DownloadCommand
{
    // No default factory — handler validates for null and emits a clear error message.
    // System.CommandLine will return null when the option is omitted.
    public static readonly Option<string?> OutputDir = new(
        "--output-dir",
        "Directory where the downloaded file will be saved");

    public static readonly Option<string> Format = new(
        "--format",
        () => "mp4",
        "Merge output container format (passed to yt-dlp --merge-output-format)");

    public static Command Build(DownloadCommandHandler handler)
    {
        var url = new Argument<string>("url", "URL of the video to download");

        var cmd = new Command("download", "Download a video from YouTube or X.com using yt-dlp");
        cmd.AddArgument(url);

        cmd.AddOption(OutputDir);
        cmd.AddOption(Format);
        cmd.AddOption(CommonOptions.DryRun);
        cmd.AddOption(CommonOptions.Yes);
        cmd.AddOption(CommonOptions.LogDir);

        cmd.SetHandler(async (InvocationContext context) =>
        {
            var outputDir = context.ParseResult.GetValueForOption(OutputDir);
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                Console.Error.WriteLine("error: --output-dir is required.");
                Console.Error.WriteLine("  Example: mt download \"https://youtu.be/...\" --output-dir /downloads");
                context.ExitCode = 2;
                return;
            }

            var options = new DownloadOptions(
                Url:               context.ParseResult.GetValueForArgument(url),
                OutputDirectory:   outputDir,
                MergeOutputFormat: context.ParseResult.GetValueForOption(Format)!,
                DryRun:            context.ParseResult.GetValueForOption(CommonOptions.DryRun),
                Yes:               context.ParseResult.GetValueForOption(CommonOptions.Yes),
                LogDir:            context.ParseResult.GetValueForOption(CommonOptions.LogDir)!
            );

            context.ExitCode = await handler.HandleAsync(options, context.GetCancellationToken());
        });

        return cmd;
    }
}
