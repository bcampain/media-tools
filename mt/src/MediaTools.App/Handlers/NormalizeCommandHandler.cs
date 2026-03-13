using MediaTools.Domain.Models;
using MediaTools.Domain.Validation;
using MediaTools.Infrastructure.Logging;
using MediaTools.Infrastructure.Notifications;
using MediaTools.Scripts;

namespace MediaTools.App.Handlers;

public class NormalizeCommandHandler(INormalizeRunner normalize, IDiscordNotifier discord, ILogSink log)
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
            TargetMode:     validated.Mode,
            Kind:           validated.Kind,
            StagingRoot:    options.StagingRoot,
            LibraryRoot:    options.LibraryRoot,
            IncomingRoot:   options.IncomingRoot,
            LogFile:        PipelineRun.ComputeLogFile(options.LogDir)
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

        var title = "🖥️ mt CLI: Invoking `normalize` script runner";
        var message =   $"""
                        Invoking script runner for **runId {runId}**
                        Target: {options.Target}, Kind: {validated.Kind}, Mode: {validated.Mode}
                        Will Execute:
                        `normalize_audio {scriptArgs}`
                        """;
        await discord.NotifyAsync(title, message, null, ct);

        // Standalone invocations don't have a manifest to update, so progress is not wired up.
        return await normalize.RunAsync(options.Target, run, scriptOptions, onProgress: null, ct);
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
