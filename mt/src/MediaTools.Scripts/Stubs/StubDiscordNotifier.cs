using MediaTools.Domain.Models;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.Scripts.Stubs;

public class StubDiscordNotifier(ILogSink log) : IDiscordNotifier
{
    public Task<int> NotifyAsync(PipelineRun run, DiscordNotifyOptions options, CancellationToken ct)
    {
        log.Info($"[stub] notify_discord \"{options.Title}\" \"{options.Message}\" (not implemented)");
        return Task.FromResult(0);
    }
}
