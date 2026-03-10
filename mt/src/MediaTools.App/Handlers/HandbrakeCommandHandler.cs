using MediaTools.Domain.Models;
using MediaTools.Domain.Validation;
using MediaTools.Infrastructure.Logging;
using MediaTools.Infrastructure.Notifications;
using MediaTools.Scripts;

namespace MediaTools.App.Handlers;

public class HandbrakeCommandHandler(IHandbrakeRunner handbrake, IDiscordNotifier discord, ILogSink log)
{
    public async Task<int> HandleAsync(HandbrakeOptions options, CancellationToken ct)
    {
        var runId = options.RunId ?? PipelineRun.GenerateRunId();

        var validation = TargetValidator.ValidateIncoming(options.Target, options.IncomingRoot);
        if (!validation.IsSuccess)
        {
            log.Error($"[handbrake] Validation failed: {validation.Error}");
            return HandlerHelpers.ValidationExitCode;
        }

        var validated = validation.Target!;
        var scriptArgs = BuildArgs(options.Target, runId, options);

        log.Info($"[handbrake] run_id={runId}");
        log.Info($"  Target: {options.Target}");
        log.Info($"  Kind:   {validated.Kind}");
        log.Info($"  Mode:   {validated.Mode}");
        log.Info("");
        log.Info("  Would invoke:");
        log.Info($"    handbrake_mp4 {scriptArgs}");

        if (options.DryRun)
            return 0;

        if (!options.Yes && !HandlerHelpers.Confirm())
        {
            log.Info("[handbrake] Cancelled.");
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
            LogDir:         options.LogDir
        );

        var scriptOptions = new HandbrakeScriptOptions(
            Quality:        options.Quality,
            Preset:         options.Preset,
            MaxDepth:       options.MaxDepth,
            Force:          options.Force,
            DryRun:         options.DryRun // Expected false due to earlier confirmation
        );

        var title = "🖥️ mt CLI: Invoking `handbrake` script runner";
        var message =   $"""
                        Invoking script runner for **runId {runId}**
                        Target: {options.Target}, Kind: {validated.Kind}, Mode: {validated.Mode}
                        Will Execute:
                        `handbrake_mp4 {scriptArgs}`
                        """;
        await discord.NotifyAsync(title, message, null, ct);

        return await handbrake.RunAsync(options.Target, run, scriptOptions, ct);
    }

    // Builds the argument string passed to the handbrake_mp4 script.
    // internal for unit testability.
    internal static string BuildArgs(string target, string runId, HandbrakeOptions options)
    {
        var parts = new List<string>
        {
            HandlerHelpers.Q(target),
            $"--run-id {runId}",
            $"--quality {options.Quality}",
            $"--preset {options.Preset}",
            $"--max-depth {options.MaxDepth}",
        };
        if (options.Force)  parts.Add("--force");
        if (options.DryRun) parts.Add("--dry-run");
        return string.Join(" ", parts);
    }
}
