using MediaTools.Domain.Models;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.Scripts;

public interface INormalizeRunner
{
    Task<int> RunAsync(
        string                    target,
        PipelineRun               run,
        NormalizeScriptOptions    options,
        Action<StepFileProgress>? onProgress,
        ILogSink                  log,
        CancellationToken         ct,
        IReadOnlySet<string>?     inheritedFiles = null);
}
