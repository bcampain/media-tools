using MediaTools.Domain.Models;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.App.Handlers;

public class PromoteCommandHandler(ILogSink log)
{
    public Task<int> HandleAsync(PromoteOptions options, CancellationToken ct)
    {
        var runId = options.RunId ?? PipelineRun.GenerateRunId();
        log.Info($"[promote] run_id={runId} target={options.Target} (not implemented)");
        return Task.FromResult(0);
    }
}
