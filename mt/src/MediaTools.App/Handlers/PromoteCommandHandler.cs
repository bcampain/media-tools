using MediaTools.Domain.Models;
using MediaTools.Domain.Validation;
using MediaTools.Infrastructure.Logging;
using MediaTools.Infrastructure.Notifications;
using MediaTools.Scripts;

namespace MediaTools.App.Handlers;

public class PromoteCommandHandler(IPromoteRunner promote, IDiscordNotifier discord, ILogSink log)
{
    public async Task<int> HandleAsync(PromoteOptions options, CancellationToken ct)
    {
        var runId = options.RunId ?? PipelineRun.GenerateRunId();

        var validation = TargetValidator.ValidateStaging(options.Target, options.StagingRoot);
        if (!validation.IsSuccess)
        {
            log.Error($"[promote] Validation failed: {validation.Error}");
            return HandlerHelpers.ValidationExitCode;
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
            return 0;

        if (!options.Yes && !HandlerHelpers.Confirm())
        {
            log.Info("[promote] Cancelled.");
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

        var scriptOptions = new PromoteScriptOptions(
            RetentionDays:  options.RetentionDays,
            Overwrite:      options.Overwrite,
            DryRun:         options.DryRun //Expected false due to earlier confirmation
        );

        var title = "🖥️ mt CLI: Invoking `promote` script runner";
        var message =   $"""
                        Invoking script runner for **runId {runId}**
                        Target: {options.Target}, Kind: {validated.Kind}, Mode: {validated.Mode}
                        Will Execute: 
                        `promote {scriptArgs}`
                        """;
        await discord.NotifyAsync(title, message, null, ct);
        
        return await promote.RunAsync(run, scriptOptions, ct);
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
