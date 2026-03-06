using MediaTools.Domain.Models;

namespace MediaTools.Scripts;

public interface IPromoteRunner
{
    Task<int> RunAsync(PipelineRun run, PromoteScriptOptions options, CancellationToken ct);
}
