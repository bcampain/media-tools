using MediaTools.Domain.Models;

namespace MediaTools.Infrastructure.Manifests;

/// <summary>
/// Writes pipeline run manifests to disk so that mt-dashboard can read them.
/// Implementations should be best-effort: a manifest write failure must never
/// cause the pipeline itself to fail.
/// </summary>
public interface IManifestWriter
{
    /// <summary>
    /// Serialises <paramref name="manifest"/> to /logs/runs/{RunId}.json,
    /// creating the directory if it does not exist.
    /// </summary>
    void Write(PipelineRunManifest manifest);
}
