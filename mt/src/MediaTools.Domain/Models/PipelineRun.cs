namespace MediaTools.Domain.Models;

/// <summary>
/// Represents a single execution run of the pipeline or an individual step.
/// RunId matches the bash script format: MMDDyyHHmmss (e.g. "030526143000").
/// </summary>
public record PipelineRun(
    string      RunId,
    DateTime    StartedAt,
    string      Target,
    TargetMode  TargetMode,
    Kind        Kind,
    string      StagingRoot,
    string      LibraryRoot,
    string      IncomingRoot,
    string      LogDir
)
{
    /// <summary>
    /// Generates a run ID matching the bash script format: date "+%m%d%y%H%M%S"
    /// Example: March 5, 2026 14:30:00 → "030526143000"
    /// </summary>
    public static string GenerateRunId() =>
        DateTime.Now.ToString("MMddyyHHmmss");

    /// <summary>
    /// Derives the expected log file path for this run, matching bash script naming:
    /// TV:           /logs/&lt;ShowName&gt;-media-tools-&lt;RunId&gt;.log
    /// Movies/Clips: /logs/&lt;kind&gt;-media-tools-&lt;RunId&gt;.log
    /// </summary>
    public string LogFilePath()
    {
        var label = Kind switch
        {
            Kind.Tv     => Path.GetFileName(Target.TrimEnd('/')),
            Kind.Movies => "movies",
            Kind.Clips  => "clips",
            _           => "unknown"
        };
        return Path.Combine(LogDir, $"{label}-media-tools-{RunId}.log");
    }
}
