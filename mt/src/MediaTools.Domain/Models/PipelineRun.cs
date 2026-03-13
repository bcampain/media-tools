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
    private const string CentralTimeZoneId = "America/Chicago";

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
        Path.Combine(logDir, $"media-tools-mt-{TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, CentralTimeZoneId):MMddyyyy}.log");

    /// <summary>
    /// Pipeline-specific top-level logfile path.
    /// Example: /logs/media-tools-mt-pipeline-030526143000.log
    /// </summary>
    public static string ComputePipelineLogFile(string logDir, string runId) =>
        Path.Combine(logDir, $"media-tools-mt-pipeline-{runId}.log");

    /// <summary>
    /// Run-scoped per-step logfile path used by native pipeline steps.
    /// Example: /logs/media-tools-mt-030526143000-handbrake.log
    /// </summary>
    public static string ComputeStepLogFile(string logDir, string runId, string stepName) =>
        Path.Combine(logDir, $"media-tools-mt-{stepName}-{runId}.log");
}
