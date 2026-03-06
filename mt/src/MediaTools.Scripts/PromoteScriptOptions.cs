namespace MediaTools.Scripts;

public record PromoteScriptOptions(
    int  RetentionDays,
    bool Overwrite,
    bool DryRun
);
