using MediaTools.Domain.Models;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.App.Handlers;

public class NotifyDiscordCommandHandler(ILogSink log)
{
    public Task<int> HandleAsync(NotifyDiscordOptions options, CancellationToken ct)
    {
        var runId = options.RunId ?? PipelineRun.GenerateRunId();
        log.Info($"[notify-discord] run_id={runId} title=\"{options.Title}\" (not implemented)");
        return Task.FromResult(0);
    }
}
