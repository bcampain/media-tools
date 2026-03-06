namespace MediaTools.App.Handlers;

public record PromoteOptions(
    string  Target,
    string  IncomingRoot,
    string  StagingRoot,
    string  LibraryRoot,
    string? RunId,
    bool    DryRun,
    bool    Yes,
    string  LogDir,
    bool    Json,
    string  Verbosity,
    int     RetentionDays,
    bool    Overwrite
);
