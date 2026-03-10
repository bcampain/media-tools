using MediaTools.Domain.Models;
using MediaTools.Infrastructure.Logging;
using MediaTools.Infrastructure.Notifications;

namespace MediaTools.App.Handlers;

public class NotifyDiscordCommandHandler(IDiscordNotifier discord, ILogSink log)
{
    public async Task<int> HandleAsync(NotifyDiscordOptions options, CancellationToken ct)
    {
        var runId = options.RunId ?? PipelineRun.GenerateRunId();

        log.Info($"[discord-notify] run_id={runId}");
        log.Info($"  Title:   {options.Title}");
        log.Info($"  Message: {options.Message}");
        if (options.Log != null) log.Info($"  Log:     {options.Log}");

        if (options.DryRun)
            return 0;

        return await discord.NotifyAsync(options.Title, options.Message, options.Log, ct);
    }
}
