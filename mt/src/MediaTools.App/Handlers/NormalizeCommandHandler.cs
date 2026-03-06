using MediaTools.Domain.Models;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.App.Handlers;

public class NormalizeCommandHandler(ILogSink log)
{
    public Task<int> HandleAsync(NormalizeOptions options, CancellationToken ct)
    {
        var runId = options.RunId ?? PipelineRun.GenerateRunId();
        log.Info($"[normalize] run_id={runId} target={options.Target} (not implemented)");
        return Task.FromResult(0);
    }
}
