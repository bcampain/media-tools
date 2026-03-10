namespace MediaTools.Domain.Models;

/// <summary>
/// A single pipeline step's execution record within a run manifest.
/// </summary>
public record StepRecord
{
    public required string   Name        { get; init; }  // "handbrake" | "normalize" | "promote"
    public required string   Status      { get; init; }  // "pending" | "running" | "complete" | "failed"
    public          DateTime? StartedAt  { get; init; }
    public          DateTime? CompletedAt { get; init; }
    public          int?      ExitCode   { get; init; }

    public StepRecord AsStarted(DateTime at) =>
        this with { Status = "running", StartedAt = at };

    public StepRecord AsCompleted(DateTime at, int exitCode) =>
        this with { Status = exitCode == 0 ? "complete" : "failed", CompletedAt = at, ExitCode = exitCode };
}

/// <summary>
/// Serialisable snapshot of a pipeline run, written to /logs/runs/{RunId}.json.
/// Updated in place as the pipeline progresses — safe to poll from the dashboard.
///
/// JSON uses snake_case keys (via ManifestWriter's JsonSerializerOptions) to match
/// Python/JS conventions on the dashboard side.
/// </summary>
public record PipelineRunManifest
{
    public required string          RunId         { get; init; }
    public required DateTime        StartedAt     { get; init; }
    public          DateTime?       CompletedAt   { get; init; }
    public required string          Kind          { get; init; }  // "tv" | "movies" | "clips"
    public required string          TargetMode    { get; init; }  // "dir" | "file"
    public required string          Target        { get; init; }
    public required string          StagingTarget { get; init; }
    public required string          LogFile       { get; init; }
    public required string          Status        { get; init; }  // "running" | "complete" | "failed" | "cancelled"
    public          int?            ExitCode      { get; init; }
    public          bool            DryRun        { get; init; }
    public required List<string>    StepsPlanned  { get; init; }
    public required List<StepRecord> Steps        { get; init; }

    /// <summary>Returns a copy with the top-level status (and optionally ExitCode/CompletedAt) updated.</summary>
    public PipelineRunManifest WithStatus(string status, int? exitCode = null, DateTime? completedAt = null) =>
        this with { Status = status, ExitCode = exitCode, CompletedAt = completedAt ?? CompletedAt };

    /// <summary>Returns a copy with the named step replaced by the result of <paramref name="update"/>.</summary>
    public PipelineRunManifest WithStep(string stepName, Func<StepRecord, StepRecord> update) =>
        this with { Steps = Steps.Select(s => s.Name == stepName ? update(s) : s).ToList() };
}
