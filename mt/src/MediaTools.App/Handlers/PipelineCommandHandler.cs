using MediaTools.Domain.Models;
using MediaTools.Domain.Validation;
using MediaTools.Infrastructure.Logging;
using MediaTools.Infrastructure.Manifests;
using MediaTools.Infrastructure.Notifications;
using MediaTools.Scripts;

namespace MediaTools.App.Handlers;

// Orchestrator pattern: coordinates the three script runners in sequence without
// delegating to the individual command handlers (HandbrakeCommandHandler, etc.).
// This avoids double Discord notifications and keeps the pipeline's own concerns
// (step skipping, abort-on-failure, inter-step notifications) in one place.
public class PipelineCommandHandler(
    IHandbrakeRunner  handbrake,
    INormalizeRunner  normalize,
    IPromoteRunner    promote,
    IDiscordNotifier  discord,
    ILogSink          log,
    IManifestWriter   manifests)
{
    // Default script options used by the pipeline. For custom quality / encoding
    // settings, run the individual commands (e.g. `mt handbrake`) directly.
    private static readonly HandbrakeScriptOptions DefaultHandbrakeOpts = new(
        Quality:  23, Preset: "fast", MaxDepth: 3, Force: false, DryRun: false);

    private static readonly NormalizeScriptOptions DefaultNormalizeOpts = new(
        TargetI: "-16", TruePeak: "-1.5", Lra: "11", StereoTrack: "on",
        OnePass: false, Force: false, DryRun: false);

    private static readonly PromoteScriptOptions DefaultPromoteOpts = new(
        RetentionDays: 30, Overwrite: false, DryRun: false);

    public async Task<int> HandleAsync(PipelineOptions options, CancellationToken ct)
    {
        var runId = options.RunId ?? PipelineRun.GenerateRunId();

        // Pipeline targets must be under incoming root (handbrake is the first step)
        var validation = TargetValidator.ValidateIncoming(options.Target, options.IncomingRoot);
        if (!validation.IsSuccess)
        {
            log.Error($"[pipeline] Validation failed: {validation.Error}");
            return HandlerHelpers.ValidationExitCode;
        }

        var validated = validation.Target!;

        // Derive the staging path that normalize and promote will receive.
        // For file targets (e.g. /incoming/movies/Alien.mkv), this returns the kind
        // directory (/staging/movies) because the exact output filename isn't known
        // until handbrake has run.
        var stagingTarget = TargetValidator.DeriveHandoffTarget(
            validated, options.IncomingRoot, options.StagingRoot);

        // Enum range comparisons work because PipelineStep is ordered:
        //   Handbrake=0, NormalizeAudio=1, Promote=2.
        // Caution: reordering the enum would silently break this logic.
        var startStep = ParseStep(options.Step) ?? PipelineStep.Handbrake;
        var untilStep = ParseStep(options.Until) ?? PipelineStep.Promote;

        bool ShouldRun(PipelineStep step) => step >= startStep && step <= untilStep;

        log.Info($"[pipeline] run_id={runId}");
        log.Info($"  Target:         {options.Target}");
        log.Info($"  Kind:           {validated.Kind}");
        log.Info($"  Mode:           {validated.Mode}");
        log.Info($"  Staging target: {stagingTarget}");
        log.Info("");
        log.Info("  Plan:");
        if (ShouldRun(PipelineStep.Handbrake))
            log.Info($"    [1/3] HandBrakeCLI (native) · target: {HandlerHelpers.Q(options.Target)} · run-id: {runId}");
        if (ShouldRun(PipelineStep.NormalizeAudio))
            log.Info($"    [2/3] normalize_audio {HandlerHelpers.Q(stagingTarget)} --run-id {runId}");
        if (ShouldRun(PipelineStep.Promote))
            log.Info($"    [3/3] promote {HandlerHelpers.Q(stagingTarget)} --run-id {runId}");

        if (options.DryRun)
            return 0;

        log.Info("[pipeline] Awaiting confirmation...");
        if (!options.Yes && !HandlerHelpers.Confirm())
        {
            log.Info("[pipeline] Cancelled.");
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
            LogDir:       options.LogDir);

        // ── Build & persist the initial manifest ─────────────────────────────
        // Written before any step runs so mt-dashboard shows the run as "running"
        // immediately. Each step updates the manifest in-place so the dashboard
        // can track fine-grained progress by polling /logs/runs/{RunId}.json.
        var stepsPlanned = new List<string>();
        if (ShouldRun(PipelineStep.Handbrake))      stepsPlanned.Add("handbrake");
        if (ShouldRun(PipelineStep.NormalizeAudio)) stepsPlanned.Add("normalize");
        if (ShouldRun(PipelineStep.Promote))        stepsPlanned.Add("promote");

        var manifest = new PipelineRunManifest
        {
            RunId         = runId,
            StartedAt     = run.StartedAt,
            Kind          = validated.Kind.ToString().ToLowerInvariant(),
            TargetMode    = validated.Mode.ToString().ToLowerInvariant(),
            Target        = options.Target,
            StagingTarget = stagingTarget,
            LogFile       = run.LogFilePath(options.Target),
            Status        = RunStatus.Running,
            DryRun        = false,
            StepsPlanned  = stepsPlanned,
            Steps         = stepsPlanned
                                .Select(s => new StepRecord { Name = s, Status = StepStatus.Pending })
                                .ToList()
        };
        manifests.Write(manifest);

        // ── Step 1: Handbrake ────────────────────────────────────────────────
        if (ShouldRun(PipelineStep.Handbrake))
        {
            log.Info("[pipeline] [1/3] Starting handbrake...");
            manifest = manifest.WithStep("handbrake", s => s.AsStarted(DateTime.UtcNow));
            manifests.Write(manifest);

            if (options.Notify)
                await discord.NotifyAsync(
                    "🖥️ mt pipeline: Step 1/3 — handbrake",
                    $"RunId {runId}\nTarget: {options.Target}", null, ct);

            // Wire up per-file progress reporting: each time a file completes, the
            // NativeHandbrakeRunner invokes this callback with a fresh StepFileProgress
            // snapshot. We update the manifest in-place so the mt-dashboard can
            // render a live progress bar ("7 of 12 files processed") while encoding runs.
            // The closure captures the manifest ref via a local variable so we can
            // update it across multiple callback invocations.
            var handbrakeManifest = manifest;
            void OnHandbrakeProgress(StepFileProgress fp)
            {
                handbrakeManifest = handbrakeManifest.WithStepFileProgress("handbrake", fp);
                manifests.Write(handbrakeManifest);
            }

            var rc = await handbrake.RunAsync(options.Target, run, DefaultHandbrakeOpts,
                onProgress: OnHandbrakeProgress, ct);

            // Sync the outer manifest reference with any progress updates that arrived
            manifest = handbrakeManifest;
            manifest = manifest.WithStep("handbrake", s => s.AsCompleted(DateTime.UtcNow, rc));
            manifests.Write(manifest);

            if (rc != 0)
            {
                log.Error($"[pipeline] handbrake failed (exit {rc}). Pipeline halted.");
                manifest = manifest.WithStatus(RunStatus.Failed, rc, DateTime.UtcNow);
                manifests.Write(manifest);

                if (options.Notify)
                    await discord.NotifyAsync(
                        "❌ mt pipeline: Failed at handbrake",
                        $"Exit code {rc} — RunId {runId}", null, ct);
                return rc;
            }

            log.Info("[pipeline] [1/3] handbrake complete.");
        }

        // ── Step 2: Normalize ────────────────────────────────────────────────
        if (ShouldRun(PipelineStep.NormalizeAudio))
        {
            log.Info("[pipeline] [2/3] Starting normalize...");
            manifest = manifest.WithStep("normalize", s => s.AsStarted(DateTime.UtcNow));
            manifests.Write(manifest);

            if (options.Notify)
                await discord.NotifyAsync(
                    "🖥️ mt pipeline: Step 2/3 — normalize",
                    $"RunId {runId}\nTarget: {stagingTarget}", null, ct);

            var rc = await normalize.RunAsync(stagingTarget, run, DefaultNormalizeOpts, ct);
            manifest = manifest.WithStep("normalize", s => s.AsCompleted(DateTime.UtcNow, rc));
            manifests.Write(manifest);

            if (rc != 0)
            {
                log.Error($"[pipeline] normalize failed (exit {rc}). Pipeline halted.");
                manifest = manifest.WithStatus(RunStatus.Failed, rc, DateTime.UtcNow);
                manifests.Write(manifest);

                if (options.Notify)
                    await discord.NotifyAsync(
                        "❌ mt pipeline: Failed at normalize",
                        $"Exit code {rc} — RunId {runId}", null, ct);
                return rc;
            }

            log.Info("[pipeline] [2/3] normalize complete.");
        }

        // ── Step 3: Promote ──────────────────────────────────────────────────
        if (ShouldRun(PipelineStep.Promote))
        {
            log.Info("[pipeline] [3/3] Starting promote...");
            manifest = manifest.WithStep("promote", s => s.AsStarted(DateTime.UtcNow));
            manifests.Write(manifest);

            if (options.Notify)
                await discord.NotifyAsync(
                    "🖥️ mt pipeline: Step 3/3 — promote",
                    $"RunId {runId}\nTarget: {stagingTarget}", null, ct);

            var rc = await promote.RunAsync(stagingTarget, run, DefaultPromoteOpts, ct);
            manifest = manifest.WithStep("promote", s => s.AsCompleted(DateTime.UtcNow, rc));
            manifests.Write(manifest);

            if (rc != 0)
            {
                log.Error($"[pipeline] promote failed (exit {rc}). Pipeline halted.");
                manifest = manifest.WithStatus(RunStatus.Failed, rc, DateTime.UtcNow);
                manifests.Write(manifest);

                if (options.Notify)
                    await discord.NotifyAsync(
                        "❌ mt pipeline: Failed at promote",
                        $"Exit code {rc} — RunId {runId}", null, ct);
                return rc;
            }

            log.Info("[pipeline] [3/3] promote complete.");
        }

        log.Info("[pipeline] All steps completed.");
        manifest = manifest.WithStatus(RunStatus.Complete, 0, DateTime.UtcNow);
        manifests.Write(manifest);

        if (options.Notify)
            await discord.NotifyAsync(
                "✅ mt pipeline: Complete",
                $"All steps finished for RunId {runId}", null, ct);

        return 0;
    }

    // Maps the --step / --until string values to the PipelineStep enum.
    // Returns null for null input (treated as "use default" by the caller).
    private static PipelineStep? ParseStep(string? s) => s?.ToLowerInvariant() switch
    {
        "handbrake" => PipelineStep.Handbrake,
        "normalize" => PipelineStep.NormalizeAudio,
        "promote"   => PipelineStep.Promote,
        _           => null
    };
}
