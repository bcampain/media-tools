namespace MediaTools.Domain.Models;

/// <summary>
/// A single pipeline step's execution record within a run manifest.
/// </summary>
public record StepRecord
{
    public required string          Name          { get; init; }      // "handbrake" | "normalize" | "promote"
    public required StepStatus      Status        { get; init; }
    public          string?         LogFile       { get; init; }
    public          DateTime?       StartedAt     { get; init; }
    public          DateTime?       CompletedAt   { get; init; }
    public          int?            ExitCode      { get; init; }
    /// <summary>
    /// Per-file progress detail populated by steps that process multiple files
    /// (e.g. native handbrake encoding). Null for steps that don't track files
    /// individually (e.g. standalone runs of handbrake/normalize/promote). When present,
    /// the dashboard renders a progress bar and per-file status list.
    /// </summary>
    public          StepFileProgress? FileProgress { get; init; }

    public StepRecord AsStarted(DateTime at) =>
        this with { Status = StepStatus.Running, StartedAt = at };

    public StepRecord AsCompleted(DateTime at, int exitCode) =>
        this with
        {
            Status      = exitCode == 0 ? StepStatus.Complete : StepStatus.Failed,
            CompletedAt = at,
            ExitCode    = exitCode
        };

    public StepRecord AsCancelled(DateTime at) =>
        this with { Status = StepStatus.Cancelled, CompletedAt = at };

    public StepRecord WithFileProgress(StepFileProgress fp) =>
        this with { FileProgress = fp };
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
    public required string          RunId              { get; init; }
    public required DateTime        StartedAt          { get; init; }
    public          DateTime?       CompletedAt        { get; init; }
    public required string          Kind               { get; init; }  // "tv" | "movies" | "clips"
    public required string          TargetMode         { get; init; }  // "dir" | "file"
    public required string          Target             { get; init; }
    public required string          StagingTarget      { get; init; }
    public required string          LogFile            { get; init; }
    public required Dictionary<string, string> StepLogFiles { get; init; }
    public required RunStatus       Status             { get; init; }
    public          int?            ExitCode           { get; init; }
    public          bool            DryRun             { get; init; }
    public required List<string>    StepsPlanned       { get; init; }
    public required List<StepRecord> Steps             { get; init; }
    public          string?         ResumedFromRunId   { get; init; }

    /// <summary>Returns a copy with the top-level status (and optionally ExitCode/CompletedAt) updated.</summary>
    public PipelineRunManifest WithStatus(RunStatus status, int? exitCode = null, DateTime? completedAt = null) =>
        this with { Status = status, ExitCode = exitCode, CompletedAt = completedAt ?? CompletedAt };

    /// <summary>Returns a copy with the named step replaced by the result of <paramref name="update"/>.</summary>
    public PipelineRunManifest WithStep(string stepName, Func<StepRecord, StepRecord> update) =>
        this with { Steps = Steps.Select(s => s.Name == stepName ? update(s) : s).ToList() };

    /// <summary>
    /// Convenience: returns a copy with the named step's FileProgress updated.
    /// Called on every file completion to keep the manifest current for the dashboard.
    /// </summary>
    public PipelineRunManifest WithStepFileProgress(string stepName, StepFileProgress fp) =>
        WithStep(stepName, s => s.WithFileProgress(fp));

    /// <summary>
    /// Returns a copy where every step still in <see cref="StepStatus.Running"/> is
    /// transitioned to <see cref="StepStatus.Cancelled"/>. Called from the cancellation
    /// handler so the dashboard never shows a step stuck in Running after a Ctrl+C.
    /// Pending steps (not yet reached) are left as-is — only the active step is affected.
    /// </summary>
    public PipelineRunManifest WithCancelledRunningSteps(DateTime at) =>
        this with
        {
            Steps = Steps.Select(s =>
            {
                if (s.Status != StepStatus.Running) return s;

                var cancelled = s.AsCancelled(at);

                // If the step tracked per-file progress, cancel any file that was
                // mid-process so the dashboard doesn't show it stuck as "running".
                if (cancelled.FileProgress?.Files is { } files)
                {
                    var updatedFiles = files
                        .Select(f => f.Status == StepStatus.Running
                            ? f with { Status = StepStatus.Cancelled, CompletedAt = at }
                            : f)
                        .ToList();

                    cancelled = cancelled with
                    {
                        FileProgress = cancelled.FileProgress with { Files = updatedFiles }
                    };
                }

                return cancelled;
            }).ToList()
        };
}
