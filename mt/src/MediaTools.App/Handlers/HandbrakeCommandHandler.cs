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

        log.Info($"[handbrake] run_id={runId}");
        log.Info($"  Target:    {options.Target}");
        log.Info($"  Kind:      {validated.Kind}");
        log.Info($"  Mode:      {validated.Mode}");
        log.Info($"  Quality:   {options.Quality}");
        log.Info($"  Preset:    {options.Preset}");
        log.Info($"  MaxDepth:  {options.MaxDepth}");
        log.Info($"  Force:     {options.Force}");
        log.Info("");
        log.Info("  Will encode with HandBrakeCLI (native — no external script).");

        if (options.DryRun)
            return 0;

        if (!options.Yes && !HandlerHelpers.Confirm())
        {
            log.Info("[handbrake] Cancelled.");
            return 0;
        }

        var run = new PipelineRun(
            RunId:        runId,
            StartedAt:    DateTime.UtcNow,
            TargetMode:   validated.Mode,
            Kind:         validated.Kind,
            StagingRoot:  options.StagingRoot,
            LibraryRoot:  options.LibraryRoot,
            IncomingRoot: options.IncomingRoot,
            LogFile:      PipelineRun.ComputeLogFile(options.LogDir)
        );

        var encodeOptions = new HandbrakeScriptOptions(
            Quality:  options.Quality,
            Preset:   options.Preset,
            MaxDepth: options.MaxDepth,
            Force:    options.Force,
            DryRun:   false  // DryRun guard handled above
        );

        var title   = "🖥️ mt CLI: Starting HandBrakeCLI encode";
        var message = $"""
                       Starting native HandBrakeCLI encode for **runId {runId}**
                       Target: {options.Target}  Kind: {validated.Kind}  Mode: {validated.Mode}
                       Quality: {options.Quality}  Preset: {options.Preset}  MaxDepth: {options.MaxDepth}
                       """;
        await discord.NotifyAsync(title, message, null, ct);

        // Standalone `mt handbrake` invocations don't have a pipeline manifest,
        // so onProgress is null — file progress is only visible on the console.
        return await handbrake.RunAsync(options.Target, run, encodeOptions, onProgress: null, ct);
    }
}
