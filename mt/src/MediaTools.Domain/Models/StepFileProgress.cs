namespace MediaTools.Domain.Models;

/// <summary>
/// Tracks per-file progress within a pipeline step that processes multiple files
/// (e.g. handbrake encoding a directory of video files).
///
/// Written into the step's manifest record so the mt-dashboard can display a
/// progress bar ("7 of 12 files processed") while the step is running.
/// </summary>
public record StepFileProgress
{
    public required int TotalFiles { get; init; }
    public required int ProcessedFiles { get; init; }  // complete + skipped + failed + inherited
    public required int FailedFiles { get; init; }
    public          int SkippedFiles { get; init; }    // output already existed; not re-encoded
    public          int InheritedFiles { get; init; }  // carried forward from a prior run via --resume
    public string? CurrentFile { get; init; }  // filename only (not full path)
    public IReadOnlyList<FileJobRecord>? Files { get; init; }  // per-file detail; may be omitted for large sets
}

/// <summary>
/// Represents one file's encode/process job within a step.
/// </summary>
public record FileJobRecord
{
    public required string InputPath { get; init; }
    public required string OutputPath { get; init; }
    public required StepStatus Status { get; init; }
    public int? ExitCode { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}
