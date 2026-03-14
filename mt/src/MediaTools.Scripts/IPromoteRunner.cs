using MediaTools.Domain.Models;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.Scripts;

public interface IPromoteRunner
{
    Task<int> RunAsync(
        string                    target,
        PipelineRun               run,
        PromoteScriptOptions      options,
        Action<StepFileProgress>? onProgress,
        ILogSink                  log,
        CancellationToken         ct);
}
