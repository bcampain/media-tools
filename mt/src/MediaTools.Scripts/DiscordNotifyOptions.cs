namespace MediaTools.Scripts;

public record DiscordNotifyOptions(
    string  Title,
    string  Message,
    string? LogPath
);
