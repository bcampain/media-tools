using MediaTools.Infrastructure.Logging;

namespace MediaTools.App.Download;

/// <summary>
/// Contract for the yt-dlp download step.
///
/// Intentionally narrow: one URL in, one DownloadResult out.
/// The runner is responsible only for launching yt-dlp and reporting
/// what it produced. Logging, Discord notifications, and user confirmation
/// are the handler's concern (same split as IHandbrakeRunner / HandbrakeCommandHandler).
///
/// Future pipeline integration:
///   PipelineCommandHandler will call RunAsync, read DownloadResult.DownloadedFiles,
///   and pass those paths as the starting target for the handbrake step.
///   No changes to this interface should be needed for that integration.
/// </summary>
public interface IDownloadRunner
{
    Task<DownloadResult> RunAsync(
        string               url,
        DownloadRunnerOptions options,
        ILogSink             log,
        CancellationToken    ct);
}
