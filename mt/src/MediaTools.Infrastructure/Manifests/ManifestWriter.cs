using System.Text.Json;
using System.Text.Json.Serialization;
using MediaTools.Domain.Models;

namespace MediaTools.Infrastructure.Manifests;

/// <summary>
/// Writes pipeline run manifests as pretty-printed JSON files to a configurable
/// directory (typically /logs/runs/).
///
/// JSON is serialised with snake_case keys so that the Python/JS dashboard can
/// consume it without any field-name mapping.
///
/// All writes are best-effort: exceptions are swallowed and printed to stderr so
/// a disk-full or permission error never brings down the pipeline.
/// </summary>
public class ManifestWriter(string runsDirectory) : IManifestWriter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented            = true,
        PropertyNamingPolicy     = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition   = JsonIgnoreCondition.WhenWritingNull,
        Converters               = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public void Write(PipelineRunManifest manifest)
    {
        try
        {
            Directory.CreateDirectory(runsDirectory);
            var path = Path.Combine(runsDirectory, $"{manifest.RunId}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOpts));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARN] ManifestWriter: could not write run manifest: {ex.Message}");
        }
    }
}
