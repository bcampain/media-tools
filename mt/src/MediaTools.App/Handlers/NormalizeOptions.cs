namespace MediaTools.App.Handlers;

public record NormalizeOptions(
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
    string  TargetI,
    string  TruePeak,
    string  Lra,
    string  StereoTrack,
    bool    OnePass,
    bool    Force
);
