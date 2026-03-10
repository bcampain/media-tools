using MediaTools.Domain.Models;

namespace MediaTools.Scripts;

public interface IProcessRunner<TOptions>
{
    Task<int> RunAsync(string target, PipelineRun run, TOptions options, CancellationToken ct);
}
