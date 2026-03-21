using System.Text.Json;
using System.Text.Json.Serialization;
using MediaTools.Domain.Models;

namespace MediaTools.Infrastructure.Manifests;

/// <summary>
/// Resolves a prior run to inherit from when --resume or --resume-from is used.

/// Candidate selection:
///   Auto  (--resume)           — most recent non-complete run whose target matches
///   Explicit (--resume-from)   — the named run, provided it is also non-complete
/// </summary>
public class RunResumeService(string runsDirectory) : IRunResumeService
{
    // Mirror the options used by ManifestWriter so round-trip fidelity is guaranteed.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        Converters             = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    // ── Public API ────────────────────────────────────────────────────────────

    public PipelineRunManifest? FindCandidate(string target)
    {
        return LoadAll()
            .Where(m => m.Target == target && m.Status != RunStatus.Complete)
            .OrderByDescending(m => m.StartedAt)
            .FirstOrDefault();
    }

    public PipelineRunManifest? LoadCandidate(string runId, out string? reason)
    {
        var path = Path.Combine(runsDirectory, $"{runId}.json");
        if (!File.Exists(path))
        {
            reason = $"manifest file not found: {path}";
            return null;
        }

        PipelineRunManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PipelineRunManifest>(
                File.ReadAllText(path), JsonOpts);
        }
        catch (Exception ex)
        {
            reason = $"could not parse manifest: {ex.Message}";
            return null;
        }

        if (manifest is null)
        {
            reason = "manifest deserialized as null";
            return null;
        }

        if (manifest.Status == RunStatus.Complete)
        {
            reason = $"run {runId} completed successfully on {manifest.CompletedAt:u} — " +
                     $"only failed or cancelled runs can be resumed";
            return null;
        }

        reason = null;
        return manifest;
    }
    
    public IReadOnlySet<string> GetInheritedInputPaths(PipelineRunManifest prior, string stepName)
    {
        var step = prior.Steps.FirstOrDefault(s => s.Name == stepName);
        if (step?.FileProgress?.Files is null)
            return ImmutableEmptySet;

        return step.FileProgress.Files
            .Where(f => f.Status == StepStatus.Complete)
            .Select(f => f.InputPath)
            .ToHashSet(StringComparer.Ordinal);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private IEnumerable<PipelineRunManifest> LoadAll()
    {
        if (!Directory.Exists(runsDirectory))
            yield break;

        foreach (var file in Directory.EnumerateFiles(runsDirectory, "*.json")
                     .Where(f => !Path.GetFileName(f).StartsWith("._", StringComparison.Ordinal)))
        {
            PipelineRunManifest? m = null;
            try
            {
                m = JsonSerializer.Deserialize<PipelineRunManifest>(
                    File.ReadAllText(file), JsonOpts);
            }
            catch (Exception ex)
            {
                // Skip unreadable / malformed manifests
                Console.Error.WriteLine($"[WARN] RunResumeService: could not load run manifest {file} : {ex.Message}");
            }

            if (m is not null)
                yield return m;
        }
    }

    private static readonly IReadOnlySet<string> ImmutableEmptySet =
        new HashSet<string>(StringComparer.Ordinal);
}
