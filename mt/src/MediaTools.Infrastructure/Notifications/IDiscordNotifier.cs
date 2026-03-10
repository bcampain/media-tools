namespace MediaTools.Infrastructure.Notifications;

public interface IDiscordNotifier
{
    Task<int> NotifyAsync(string title, string message, string? logPath, CancellationToken ct);
}
