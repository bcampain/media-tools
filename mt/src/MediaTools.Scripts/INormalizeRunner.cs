using MediaTools.Domain.Models;

namespace MediaTools.Scripts;

public interface INormalizeRunner
{
    Task<int> RunAsync(
        string                    target,
        PipelineRun               run,
        NormalizeScriptOptions    options,
        Action<StepFileProgress>? onProgress,
        CancellationToken         ct);
}
