using MediaTools.Domain.Models;
using MediaTools.Domain.Validation;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.App.Handlers;

public class PromoteCommandHandler(ILogSink log)
{
    public Task<int> HandleAsync(PromoteOptions options, CancellationToken ct)
    {
        var runId = options.RunId ?? PipelineRun.GenerateRunId();

        var validation = TargetValidator.ValidateStaging(options.Target, options.StagingRoot);
        if (!validation.IsSuccess)
        {
            log.Error($"[promote] Validation failed: {validation.Error}");
            return Task.FromResult(HandlerHelpers.ValidationExitCode);
        }

        var validated = validation.Target!;
        var scriptArgs = BuildArgs(options.Target, runId, options);

        log.Info($"[promote] run_id={runId}");
        log.Info($"  Target: {options.Target}");
        log.Info($"  Kind:   {validated.Kind}");
        log.Info($"  Mode:   {validated.Mode}");
        log.Info("");
        log.Info("  Would invoke:");
        log.Info($"    promote {scriptArgs}");

        if (options.DryRun)
            return Task.FromResult(0);

        if (!options.Yes && !HandlerHelpers.Confirm())
        {
            log.Info("[promote] Cancelled.");
            return Task.FromResult(0);
        }

        // M3: replace this stub with IPromoteRunner.RunAsync()
        log.Info("[promote] (stub — script execution not yet implemented)");
        return Task.FromResult(0);
    }

    // internal for unit testability.
    internal static string BuildArgs(string target, string runId, PromoteOptions options)
    {
        var parts = new List<string>
        {
            HandlerHelpers.Q(target),
            $"--run-id {runId}",
            $"--retention-days {options.RetentionDays}",
        };
        if (options.Overwrite) parts.Add("--overwrite");
        if (options.DryRun)    parts.Add("--dry-run");
        return string.Join(" ", parts);
    }
}
