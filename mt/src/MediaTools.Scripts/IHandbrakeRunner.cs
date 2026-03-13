using MediaTools.Domain.Models;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.Scripts;

/// <summary>
/// Contract for the handbrake encoding step.
///
/// Unlike the other runners (normalize, promote) which still delegate to shell scripts
/// and implement the generic <IProcessRunner>, the handbrake runner is now a
/// native C# implementation that processes multiple files and reports per-file progress
/// so the mt-dashboard can display a progress bar while encoding is running.
///
/// The <paramref name="onProgress"/> callback is optional:
/// - PipelineCommandHandler provides a callback that writes file progress to the manifest.
/// - HandbrakeCommandHandler (standalone `mt handbrake` invocations) passes null
///   because no pipeline manifest exists in that context.
/// </summary>
public interface IHandbrakeRunner
{
    Task<int> RunAsync(
        string                     target,
        PipelineRun                run,
        HandbrakeScriptOptions     options,
        Action<StepFileProgress>?  onProgress,
        ILogSink                   log,
        CancellationToken          ct);
}
