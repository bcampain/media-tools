using MediaTools.Domain.Models;

namespace MediaTools.Scripts;

public interface IHandbrakeRunner
{
    Task<int> RunAsync(PipelineRun run, HandbrakeScriptOptions options, CancellationToken ct);
}
