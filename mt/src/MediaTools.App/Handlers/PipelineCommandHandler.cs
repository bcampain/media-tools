using MediaTools.Domain.Models;
using MediaTools.Domain.Validation;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.App.Handlers;

public class PipelineCommandHandler(ILogSink log)
{
    public Task<int> HandleAsync(PipelineOptions options, CancellationToken ct)
    {
        var runId = options.RunId ?? PipelineRun.GenerateRunId();

        // Pipeline targets must be under incoming root (handbrake is the first step)
        var validation = TargetValidator.ValidateIncoming(options.Target, options.IncomingRoot);
        if (!validation.IsSuccess)
        {
            log.Error($"[pipeline] Validation failed: {validation.Error}");
            return Task.FromResult(HandlerHelpers.ValidationExitCode);
        }

        var validated = validation.Target!;

        // Derive the staging path that normalize and promote will receive.
        // For file targets (e.g. /incoming/movies/Alien.mkv), this returns the kind
        // directory (/staging/movies) because the exact output filename isn't known
        // until handbrake has run.
        var stagingTarget = TargetValidator.DeriveHandoffTarget(
            validated, options.IncomingRoot, options.StagingRoot);

        log.Info($"[pipeline] run_id={runId}");
        log.Info($"  Target:         {options.Target}");
        log.Info($"  Kind:           {validated.Kind}");
        log.Info($"  Mode:           {validated.Mode}");
        log.Info($"  Staging target: {stagingTarget}");
        log.Info("");
        log.Info("  Plan:");
        log.Info($"    [1/3] handbrake_mp4 {HandlerHelpers.Q(options.Target)} --run-id {runId}");
        log.Info($"    [2/3] normalize_audio {HandlerHelpers.Q(stagingTarget)} --run-id {runId}");
        log.Info($"    [3/3] promote {HandlerHelpers.Q(stagingTarget)} --run-id {runId}");

        if (options.DryRun)
            return Task.FromResult(0);

        if (!options.Yes && !HandlerHelpers.Confirm())
        {
            log.Info("[pipeline] Cancelled.");
            return Task.FromResult(0);
        }

        // M3: replace these stubs with sequential runner calls + discord notifications
        log.Info("[pipeline] (stub — script execution not yet implemented)");
        return Task.FromResult(0);
    }
}
