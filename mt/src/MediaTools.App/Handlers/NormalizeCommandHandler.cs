using MediaTools.Domain.Models;
using MediaTools.Domain.Validation;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.App.Handlers;

public class NormalizeCommandHandler(ILogSink log)
{
    public Task<int> HandleAsync(NormalizeOptions options, CancellationToken ct)
    {
        var runId = options.RunId ?? PipelineRun.GenerateRunId();

        var validation = TargetValidator.ValidateStaging(options.Target, options.StagingRoot);
        if (!validation.IsSuccess)
        {
            log.Error($"[normalize] Validation failed: {validation.Error}");
            return Task.FromResult(HandlerHelpers.ValidationExitCode);
        }

        var validated = validation.Target!;
        var scriptArgs = BuildArgs(options.Target, runId, options);

        log.Info($"[normalize] run_id={runId}");
        log.Info($"  Target: {options.Target}");
        log.Info($"  Kind:   {validated.Kind}");
        log.Info($"  Mode:   {validated.Mode}");
        log.Info("");
        log.Info("  Would invoke:");
        log.Info($"    normalize_audio {scriptArgs}");

        if (options.DryRun)
            return Task.FromResult(0);

        if (!options.Yes && !HandlerHelpers.Confirm())
        {
            log.Info("[normalize] Cancelled.");
            return Task.FromResult(0);
        }

        // M3: replace this stub with INormalizeRunner.RunAsync()
        log.Info("[normalize] (stub — script execution not yet implemented)");
        return Task.FromResult(0);
    }

    // internal for unit testability.
    internal static string BuildArgs(string target, string runId, NormalizeOptions options)
    {
        var parts = new List<string>
        {
            HandlerHelpers.Q(target),
            $"--run-id {runId}",
            $"--target-i {options.TargetI}",
            $"--true-peak {options.TruePeak}",
            $"--lra {options.Lra}",
            $"--stereo-track {options.StereoTrack}",
        };
        if (options.OnePass) parts.Add("--one-pass");
        if (options.Force)   parts.Add("--force");
        if (options.DryRun)  parts.Add("--dry-run");
        return string.Join(" ", parts);
    }
}
