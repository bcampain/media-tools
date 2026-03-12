namespace MediaTools.Domain.Models;

/// <summary>
/// Represents a single execution run of the pipeline or an individual step.
/// RunId matches the bash script format: MMDDyyHHmmss (e.g. "030526143000").
/// </summary>
public record PipelineRun(
    string      RunId,
    DateTime    StartedAt,
    TargetMode  TargetMode,
    Kind        Kind,
    string      StagingRoot,
    string      LibraryRoot,
    string      IncomingRoot,
    string      LogFile
)
{
    /// <summary>
    /// Generates a run ID matching the bash script format: date "+%m%d%y%H%M%S"
    /// Example: March 5, 2026 14:30:00 → "030526143000"
    /// </summary>
    public static string GenerateRunId() =>
        DateTime.Now.ToString("MMddyyHHmmss");

    /// <summary>
    /// Single source of truth for the log file path convention used across all
    /// handlers and runners. Call once per run at construction time to avoid
    /// any theoretical midnight-rollover skew between callers.
    /// </summary>
    public static string ComputeLogFile(string logDir) =>
        Path.Combine(logDir, $"media-tools-mt-{DateTime.UtcNow:MMddyyyy}.log");
}
