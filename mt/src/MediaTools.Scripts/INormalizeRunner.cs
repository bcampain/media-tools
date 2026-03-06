using MediaTools.Domain.Models;

namespace MediaTools.Scripts;

public interface INormalizeRunner
{
    Task<int> RunAsync(PipelineRun run, NormalizeScriptOptions options, CancellationToken ct);
}
