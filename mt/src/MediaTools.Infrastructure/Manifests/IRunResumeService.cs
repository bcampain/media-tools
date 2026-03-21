using MediaTools.Domain.Models;

namespace MediaTools.Infrastructure.Manifests;

/// <summary>
/// Resolves a prior pipeline run to inherit completed files from when
/// --resume or --resume-from is used.
/// </summary>
public interface IRunResumeService
{
    /// <summary>
    /// Scans the runs directory for the most recent non-complete run whose
    /// <see cref="PipelineRunManifest.Target"/> equals <paramref name="target"/>.
    /// Returns null if no eligible candidate exists.
    /// </summary>
    PipelineRunManifest? FindCandidate(string target);

    /// <summary>
    /// Loads the manifest for <paramref name="runId"/> and returns it if it is
    /// not Complete. Returns null (and populates <paramref name="reason"/>) when
    /// the run cannot be used as a resume source.
    /// </summary>
    PipelineRunManifest? LoadCandidate(string runId, out string? reason);

    /// <summary>
    /// Returns the set of <see cref="FileJobRecord.InputPath"/> values from the named
    /// step in <paramref name="prior"/> that have <see cref="StepStatus.Complete"/> status.
    /// These are the files the new run should mark as <see cref="StepStatus.Inherited"/>
    /// instead of re-processing.
    /// </summary>
    IReadOnlySet<string> GetInheritedInputPaths(PipelineRunManifest prior, string stepName);
}
