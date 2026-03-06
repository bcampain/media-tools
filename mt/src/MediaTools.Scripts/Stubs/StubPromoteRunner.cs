using MediaTools.Domain.Models;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.Scripts.Stubs;

public class StubPromoteRunner(ILogSink log) : IPromoteRunner
{
    public Task<int> RunAsync(PipelineRun run, PromoteScriptOptions options, CancellationToken ct)
    {
        log.Info($"[stub] promote {run.Target} --run-id {run.RunId} (not implemented)");
        return Task.FromResult(0);
    }
}
