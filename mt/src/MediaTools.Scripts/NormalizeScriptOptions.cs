namespace MediaTools.Scripts;

public record NormalizeScriptOptions(
    string TargetI,
    string TruePeak,
    string Lra,
    string StereoTrack,
    bool   OnePass,
    bool   Force,
    bool   DryRun
);
