using MediaTools.App.Download;
using MediaTools.Infrastructure.Logging;
using MediaTools.Infrastructure.Notifications;

namespace MediaTools.App.Handlers;

/// <summary>
/// Orchestrates the `mt download` command.
///
/// Responsibilities (mirrors the other command handlers):
///   1. Log what is about to happen.
///   2. Confirm with the user unless --yes or --dry-run.
///   3. Send a Discord notification that the download is starting.
///   4. Delegate to IDownloadRunner.
///   5. Send a Discord notification with the outcome.
///   6. Return the appropriate exit code.
///
/// Note on Discord notifications:
///   Unlike the pipeline steps, this command doesn't have a run manifest or a
///   log-file attachment (there's no PipelineRun).  The Discord messages are
///   intentionally lightweight — just a URL + result summary.
///
/// Future pipeline integration:
///   When `mt pipeline` grows a download-first mode, PipelineCommandHandler will
///   call IDownloadRunner directly (reading DownloadResult.DownloadedFiles to seed
///   the handbrake step) rather than going through this handler.  This handler
///   remains the entry point for standalone `mt download` invocations.
/// </summary>
public class DownloadCommandHandler(
    IDownloadRunner  download,
    IDiscordNotifier discord,
    ILogSink         log)
{
    public async Task<int> HandleAsync(DownloadOptions options, CancellationToken ct)
    {
        log.Info("[download] mt download");
        log.Info($"  URL:     {options.Url}");
        log.Info($"  Output:  {options.OutputDirectory}");
        log.Info($"  Format:  {options.MergeOutputFormat}");
        log.Info($"  DryRun:  {options.DryRun}");
        log.Info("");

        if (options.DryRun)
        {
            log.Info("  DRYRUN — will simulate without downloading.");
        }
        else if (!options.Yes && !HandlerHelpers.Confirm())
        {
            log.Info("[download] Cancelled.");
            return 0;
        }

        await discord.NotifyAsync(
            "⬇️ mt download: Starting",
            $"URL: {options.Url}\nOutput: {options.OutputDirectory}",
            logPath: null, ct);

        var runnerOptions = new DownloadRunnerOptions(
            OutputDirectory:   options.OutputDirectory,
            MergeOutputFormat: options.MergeOutputFormat,
            DryRun:            options.DryRun
        );

        var result = await download.RunAsync(options.Url, runnerOptions, log, ct);

        if (result.ExitCode == 0 && !options.DryRun)
        {
            var fileList = result.DownloadedFiles.Count > 0
                ? string.Join("\n", result.DownloadedFiles.Select(f => $"• {Path.GetFileName(f)}"))
                : "(no files tracked)";

            await discord.NotifyAsync(
                "✅ mt download: Complete",
                $"URL: {options.Url}\nOutput: {options.OutputDirectory}\n{fileList}",
                logPath: null, ct);
        }
        else if (result.ExitCode != 0)
        {
            await discord.NotifyAsync(
                "❌ mt download: Failed",
                $"URL: {options.Url}\nyt-dlp exited with code {result.ExitCode}",
                logPath: null, ct);
        }

        return result.ExitCode;
    }
}
