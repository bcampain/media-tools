using MediaTools.Domain.Models;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.App.Handlers;

public class PipelineCommandHandler(ILogSink log)
{
    public Task<int> HandleAsync(PipelineOptions options, CancellationToken ct)
    {
        var runId = options.RunId ?? PipelineRun.GenerateRunId();
        log.Info($"[pipeline] run_id={runId} target={options.Target} (not implemented)");
        return Task.FromResult(0);
    }
}
