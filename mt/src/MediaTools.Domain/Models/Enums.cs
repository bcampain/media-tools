namespace MediaTools.Domain.Models;

public enum PipelineStep
{
    Handbrake,
    NormalizeAudio,
    Promote
}

public enum Kind
{
    Tv,
    Movies,
    Clips
}

public enum TargetMode
{
    Dir,
    File
}

public enum Verbosity
{
    Quiet,
    Normal,
    Verbose
}

/// <summary>
/// Overall status of a pipeline run stored in <see cref="PipelineRunManifest"/>.
/// Serialised to lowercase by <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/>
/// </summary>
public enum RunStatus
{
    Running,
    Complete,
    Failed,
    Cancelled
}

/// <summary>
/// Status of an individual pipeline step (<see cref="StepRecord"/>) or a
/// per-file encode job (<see cref="FileJobRecord"/>).
/// Serialised the same way as <see cref="RunStatus"/> — lowercase strings in JSON.
/// </summary>
public enum StepStatus
{
    Pending,
    Running,
    Complete,
    Failed,
    Cancelled,
    Skipped
}
