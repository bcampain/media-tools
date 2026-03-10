namespace MediaTools.App.Handlers;

public record NotifyDiscordOptions(
    string  Title,
    string  Message,
    string? Log,
    string? RunId,
    bool    DryRun,
    string  LogDir,
    bool    Json,
    string  Verbosity
);
