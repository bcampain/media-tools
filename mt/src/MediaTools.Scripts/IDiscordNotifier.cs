using MediaTools.Domain.Models;

namespace MediaTools.Scripts;

public interface IDiscordNotifier
{
    Task<int> NotifyAsync(PipelineRun run, DiscordNotifyOptions options, CancellationToken ct);
}
