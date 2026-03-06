using MediaTools.Domain.Models;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.App.Handlers;

public class HandbrakeCommandHandler(ILogSink log)
{
    public Task<int> HandleAsync(HandbrakeOptions options, CancellationToken ct)
    {
        var runId = options.RunId ?? PipelineRun.GenerateRunId();
        log.Info($"[handbrake] run_id={runId} target={options.Target} (not implemented)");
        return Task.FromResult(0);
    }
}
