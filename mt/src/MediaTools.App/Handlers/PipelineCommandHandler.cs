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
    IManifestWriter   manifests)
{
    // Default script options used by the pipeline. For custom quality / encoding
    // settings, run the individual commands (e.g. `mt handbrake`) directly.
    private static readonly HandbrakeScriptOptions DefaultHandbrakeOpts = new(
        Quality:  23, Preset: "fast", MaxDepth: 3, Force: false, DryRun: false);

    private static readonly NormalizeScriptOptions DefaultNormalizeOpts = new(
        TargetI: "-16", TruePeak: "-1.5", Lra: "11", StereoTrack: "on",
        OnePass: false, Force: false, DryRun: false);

    // ArchiveRoot mirrors the default in CommonOptions.ArchiveRoot ("/archive").
    // It is not wired through PipelineOptions because archive is a promote-only concern.
    private static readonly PromoteScriptOptions DefaultPromoteOpts = new(
        RetentionDays: 30, Overwrite: false, DryRun: false, ArchiveRoot: "/archive");

    public async Task<int> HandleAsync(PipelineOptions options, CancellationToken ct)
    {
        var runId = options.RunId ?? PipelineRun.GenerateRunId();
        using var pipelineLog = new TeeLogSink(
            new ConsoleLogSink(),
            PipelineRun.ComputePipelineLogFile(options.LogDir, runId));

        // Pipeline targets must be under incoming root (handbrake is the first step)
        var validation = TargetValidator.ValidateIncoming(options.Target, options.IncomingRoot);
        if (!validation.IsSuccess)
        {
            pipelineLog.Error($"[pipeline] Validation failed: {validation.Error}");
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

        pipelineLog.Info($"[pipeline] run_id={runId}");
        pipelineLog.Info($"  Target:         {options.Target}");
        pipelineLog.Info($"  Kind:           {validated.Kind}");
        pipelineLog.Info($"  Mode:           {validated.Mode}");
        pipelineLog.Info($"  Staging target: {stagingTarget}");
        pipelineLog.Info("");
        pipelineLog.Info("  Plan:");
        if (ShouldRun(PipelineStep.Handbrake))
            pipelineLog.Info($"    [1/3] HandBrakeCLI (native) · target: {HandlerHelpers.Q(options.Target)} · run-id: {runId}");
        if (ShouldRun(PipelineStep.NormalizeAudio))
            pipelineLog.Info($"    [2/3] normalize_audio {HandlerHelpers.Q(stagingTarget)} --run-id {runId}");
        if (ShouldRun(PipelineStep.Promote))
            pipelineLog.Info($"    [3/3] promote {HandlerHelpers.Q(stagingTarget)} --run-id {runId}");

        if (options.DryRun)
            return 0;

        pipelineLog.Info("[pipeline] Awaiting confirmation...");
        if (!options.Yes && !HandlerHelpers.Confirm())
        {
            pipelineLog.Info("[pipeline] Cancelled.");
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
            LogFile:      PipelineRun.ComputePipelineLogFile(options.LogDir, runId));

        // ── Build & persist the initial manifest ─────────────────────────────
        // Written before any step runs so mt-dashboard shows the run as "running"
        // immediately. Each step updates the manifest in-place so the dashboard
        // can track fine-grained progress by polling /logs/runs/{RunId}.json.
        var stepsPlanned = new List<string>();
        if (ShouldRun(PipelineStep.Handbrake))      stepsPlanned.Add("handbrake");
        if (ShouldRun(PipelineStep.NormalizeAudio)) stepsPlanned.Add("normalize");
        if (ShouldRun(PipelineStep.Promote))        stepsPlanned.Add("promote");

        // Derive log file paths from the planned steps so the dictionary keys
        // always match exactly — no risk of KeyNotFoundException if a step is
        // added or skipped via --step / --until.
        var stepLogFiles = stepsPlanned.ToDictionary(
            s => s,
            s => PipelineRun.ComputeStepLogFile(options.LogDir, runId, s));

        var manifest = new PipelineRunManifest
        {
            RunId         = runId,
            StartedAt     = run.StartedAt,
            Kind          = validated.Kind.ToString().ToLowerInvariant(),
            TargetMode    = validated.Mode.ToString().ToLowerInvariant(),
            Target        = options.Target,
            StagingTarget = stagingTarget,
            LogFile       = run.LogFile,
            StepLogFiles      = stepLogFiles,
            Status        = RunStatus.Running,
            DryRun        = false,
            StepsPlanned  = stepsPlanned,
            Steps         = stepsPlanned
                                .Select(s => new StepRecord
                                {
                                    Name    = s,
                                    Status  = StepStatus.Pending,
                                    LogFile = stepLogFiles[s]
                                })
                                .ToList()
        };
        manifests.Write(manifest);

        try
        {

        // ── Step 1: Handbrake ────────────────────────────────────────────────
        if (ShouldRun(PipelineStep.Handbrake))
        {
            pipelineLog.Info("[pipeline] [1/3] Starting handbrake...");
            manifest = manifest.WithStep("handbrake", s => s.AsStarted(DateTime.UtcNow));
            manifests.Write(manifest);

            if (options.Notify)
                await discord.NotifyAsync(
                    "🖥️ mt pipeline: Step 1/3 — handbrake STARTED",
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
                manifest = handbrakeManifest; // keep outer ref live so the cancellation handler sees latest file progress
                manifests.Write(handbrakeManifest);
            }

            using var handbrakeLog = new TeeLogSink(new ConsoleLogSink(), stepLogFiles["handbrake"]);
            var rc = await handbrake.RunAsync(options.Target, run, DefaultHandbrakeOpts,
                onProgress: OnHandbrakeProgress, log: handbrakeLog, ct);

            manifest = manifest.WithStep("handbrake", s => s.AsCompleted(DateTime.UtcNow, rc));
            manifests.Write(manifest);

            if (rc != 0)
            {
                pipelineLog.Error($"[pipeline] handbrake failed (exit {rc}). Pipeline halted.");
                manifest = manifest.WithStatus(RunStatus.Failed, rc, DateTime.UtcNow);
                manifests.Write(manifest);

                if (options.Notify)
                    await discord.NotifyAsync(
                        "❌ mt pipeline: Failed at handbrake",
                        $"Exit code {rc} — RunId {runId}", manifest.LogFile, ct);
                return rc;
            }

            pipelineLog.Info("[pipeline] [1/3] handbrake complete.");
        }

        // ── Step 2: Normalize ────────────────────────────────────────────────
        if (ShouldRun(PipelineStep.NormalizeAudio))
        {
            pipelineLog.Info("[pipeline] [2/3] Starting normalize...");
            manifest = manifest.WithStep("normalize", s => s.AsStarted(DateTime.UtcNow));
            manifests.Write(manifest);

            if (options.Notify)
                await discord.NotifyAsync(
                    "🖥️ mt pipeline: Step 2/3 — normalize STARTED",
                    $"RunId {runId}\nTarget: {stagingTarget}", null, ct);

            // Per-file progress reporting for the normalize step
            var normalizeManifest = manifest;
            void OnNormalizeProgress(StepFileProgress fp)
            {
                normalizeManifest = normalizeManifest.WithStepFileProgress("normalize", fp);
                manifest = normalizeManifest; // keep outer ref live so the cancellation handler sees latest file progress
                manifests.Write(normalizeManifest);
            }

            using var normalizeLog = new TeeLogSink(new ConsoleLogSink(), stepLogFiles["normalize"]);
            var rc = await normalize.RunAsync(stagingTarget, run, DefaultNormalizeOpts,
                onProgress: OnNormalizeProgress, log: normalizeLog, ct);

            // manifest is already up-to-date via OnNormalizeProgress; stamp the step result
            manifest = manifest.WithStep("normalize", s => s.AsCompleted(DateTime.UtcNow, rc));
            manifests.Write(manifest);

            if (rc != 0)
            {
                pipelineLog.Error($"[pipeline] normalize failed (exit {rc}). Pipeline halted.");
                manifest = manifest.WithStatus(RunStatus.Failed, rc, DateTime.UtcNow);
                manifests.Write(manifest);

                if (options.Notify)
                    await discord.NotifyAsync(
                        "❌ mt pipeline: Failed at normalize",
                        $"Exit code {rc} — RunId {runId}", manifest.LogFile, ct);
                return rc;
            }

            pipelineLog.Info("[pipeline] [2/3] normalize complete.");
        }

        // ── Step 3: Promote ──────────────────────────────────────────────────
        if (ShouldRun(PipelineStep.Promote))
        {
            pipelineLog.Info("[pipeline] [3/3] Starting promote...");
            manifest = manifest.WithStep("promote", s => s.AsStarted(DateTime.UtcNow));
            manifests.Write(manifest);

            if (options.Notify)
                await discord.NotifyAsync(
                    "🖥️ mt pipeline: Step 3/3 — promote STARTED",
                    $"RunId {runId}\nTarget: {stagingTarget}", null, ct);

            var promoteManifest = manifest;
            void OnPromoteProgress(StepFileProgress fp)
            {
                promoteManifest = promoteManifest.WithStepFileProgress("promote", fp);
                manifest = promoteManifest;
                manifests.Write(promoteManifest);
            }

            using var promoteLog = new TeeLogSink(new ConsoleLogSink(), stepLogFiles["promote"]);
            var rc = await promote.RunAsync(stagingTarget, run, DefaultPromoteOpts,
                onProgress: OnPromoteProgress, log: promoteLog, ct);
            manifest = manifest.WithStep("promote", s => s.AsCompleted(DateTime.UtcNow, rc));
            manifests.Write(manifest);

            if (rc != 0)
            {
                pipelineLog.Error($"[pipeline] promote failed (exit {rc}). Pipeline halted.");
                manifest = manifest.WithStatus(RunStatus.Failed, rc, DateTime.UtcNow);
                manifests.Write(manifest);

                if (options.Notify)
                    await discord.NotifyAsync(
                        "❌ mt pipeline: Failed at promote",
                        $"Exit code {rc} — RunId {runId}", manifest.LogFile, ct);
                return rc;
            }

            pipelineLog.Info("[pipeline] [3/3] promote complete.");
        }

        pipelineLog.Info("[pipeline] All steps completed.");
        manifest = manifest.WithStatus(RunStatus.Complete, 0, DateTime.UtcNow);
        manifests.Write(manifest);

        if (options.Notify)
            await discord.NotifyAsync(
                "✅ mt pipeline: Complete",
                $"All steps finished for RunId {runId}", null, ct);

        return 0;

        } // end try
        catch (OperationCanceledException)
        {
            // Ctrl+C was pressed. The runner that was active already killed its
            // child process and re-threw, so we just need to stamp the manifest.
            pipelineLog.Warn("[pipeline] Run cancelled by user (Ctrl+C).");
            var cancelledAt = DateTime.UtcNow;
            // Mark the step that was mid-run as Cancelled so the dashboard doesn't
            // show it stuck in "running". Pending steps that hadn't started are untouched.
            manifest = manifest.WithCancelledRunningSteps(cancelledAt)
                               .WithStatus(RunStatus.Cancelled, exitCode: 130, cancelledAt);
            manifests.Write(manifest);

            if (options.Notify)
                await discord.NotifyAsync(
                    "⚠️ mt pipeline: Cancelled",
                    $"Run {runId} was cancelled by the user.", manifest.LogFile,
                    CancellationToken.None); // ct is already signalled — use None here

            // 130 = 128 + SIGINT(2): the conventional shell exit code for Ctrl+C,
            // matching what bash scripts return when interrupted the same way.
            return 130;
        }
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
