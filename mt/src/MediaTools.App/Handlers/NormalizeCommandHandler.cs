using MediaTools.Domain.Models;
using MediaTools.Domain.Validation;
using MediaTools.Infrastructure.Logging;
using MediaTools.Scripts;

namespace MediaTools.App.Handlers;

public class NormalizeCommandHandler(INormalizeRunner normalize, ILogSink log)
{
    public async Task<int> HandleAsync(NormalizeOptions options, CancellationToken ct)
    {
        var runId = options.RunId ?? PipelineRun.GenerateRunId();

        var validation = TargetValidator.ValidateStaging(options.Target, options.StagingRoot);
        if (!validation.IsSuccess)
        {
            log.Error($"[normalize] Validation failed: {validation.Error}");
            return HandlerHelpers.ValidationExitCode;
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
            return 0;

        if (!options.Yes && !HandlerHelpers.Confirm())
        {
            log.Info("[normalize] Cancelled.");
            return 0;
        }

        var run = new PipelineRun(
            RunId:          runId,
            StartedAt:      DateTime.UtcNow,
            Target:         options.Target,
            TargetMode:     validated.Mode,
            Kind:           validated.Kind,
            StagingRoot:    options.StagingRoot,
            LibraryRoot:    options.LibraryRoot,
            IncomingRoot:   options.IncomingRoot,
            LogDir:         options.LogDir
        );

        var scriptOptions = new NormalizeScriptOptions(
            TargetI:        options.TargetI,
            TruePeak:       options.TruePeak,
            Lra:            options.Lra,
            StereoTrack:    options.StereoTrack,
            OnePass:        options.OnePass,
            Force:          options.Force,
            DryRun:         options.DryRun //Expected false due to earlier confirmation
        );
        
        return await normalize.RunAsync(run, scriptOptions, ct);
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
            $"--stereo-track {options.StereoTrack}"
        };
        if (options.OnePass) parts.Add("--one-pass");
        if (options.Force)   parts.Add("--force");
        if (options.DryRun)  parts.Add("--dry-run");
        return string.Join(" ", parts);
    }
}
