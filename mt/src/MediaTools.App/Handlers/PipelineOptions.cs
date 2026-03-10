namespace MediaTools.App.Handlers;

public record PipelineOptions(
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
    bool    StopOnError,
    bool    Notify,
    string? Step,
    string? Until
);
