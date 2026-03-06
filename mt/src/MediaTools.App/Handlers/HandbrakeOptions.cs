namespace MediaTools.App.Handlers;

public record HandbrakeOptions(
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
    int     Quality,
    string  Preset,
    int     MaxDepth,
    bool    Force
);
