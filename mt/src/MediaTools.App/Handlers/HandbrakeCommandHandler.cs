using MediaTools.Domain.Models;
using MediaTools.Domain.Validation;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.App.Handlers;

public class HandbrakeCommandHandler(ILogSink log)
{
    public Task<int> HandleAsync(HandbrakeOptions options, CancellationToken ct)
    {
        var runId = options.RunId ?? PipelineRun.GenerateRunId();

        var validation = TargetValidator.ValidateIncoming(options.Target, options.IncomingRoot);
        if (!validation.IsSuccess)
        {
            log.Error($"[handbrake] Validation failed: {validation.Error}");
            return Task.FromResult(HandlerHelpers.ValidationExitCode);
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
            return Task.FromResult(0);

        if (!options.Yes && !HandlerHelpers.Confirm())
        {
            log.Info("[handbrake] Cancelled.");
            return Task.FromResult(0);
        }

        // M3: replace this stub with IHandbrakeRunner.RunAsync()
        log.Info("[handbrake] (stub — script execution not yet implemented)");
        return Task.FromResult(0);
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
