namespace MediaTools.Scripts;

public record HandbrakeScriptOptions(
    int    Quality,
    string Preset,
    int    MaxDepth,
    bool   Force,
    bool   DryRun
);
