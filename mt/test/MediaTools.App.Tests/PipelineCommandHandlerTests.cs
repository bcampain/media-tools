using FluentAssertions;
using MediaTools.App.Handlers;
using MediaTools.Domain.Models;
using MediaTools.Infrastructure.Logging;
using MediaTools.Infrastructure.Manifests;
using MediaTools.Infrastructure.Notifications;
using MediaTools.Scripts;

namespace MediaTools.App.Tests;

/// <summary>
/// Tests for PipelineCommandHandler.
///
/// We can test validation failure paths without any mocking because they return
/// before reaching the confirmation prompt or runner calls. The confirmation prompt
/// (Console.ReadLine) is not exercised here — that path requires manual testing.
/// All valid-target tests use --dry-run so they also exit before the prompt.
///
/// The null-stub inner classes below are idiomatic xUnit test doubles: lightweight
/// in-process fakes that satisfy an interface without any test-framework mocking
/// library. They're appropriate here because the tests never actually execute the
/// pipeline steps.
/// </summary>
public class PipelineCommandHandlerTests
{
    // ── Null stubs ───────────────────────────────────────────────────────────

    private class NullHandbrakeRunner : IHandbrakeRunner
    {
        public Task<int> RunAsync(
            string target, PipelineRun run, HandbrakeScriptOptions options,
            Action<StepFileProgress>? onProgress, ILogSink log, CancellationToken ct,
            IReadOnlySet<string>? inheritedFiles = null)
            => Task.FromResult(0);
    }

    private class NullNormalizeRunner : INormalizeRunner
    {
        public Task<int> RunAsync(string target, PipelineRun run, NormalizeScriptOptions options,
                                  Action<StepFileProgress>? onProgress, ILogSink log, CancellationToken ct,
                                  IReadOnlySet<string>? inheritedFiles = null)
            => Task.FromResult(0);
    }

    private class NullPromoteRunner : IPromoteRunner
    {
        public Task<int> RunAsync(string target, PipelineRun run, PromoteScriptOptions options,
                                  Action<StepFileProgress>? onProgress, ILogSink log, CancellationToken ct,
                                  IReadOnlySet<string>? inheritedFiles = null)
            => Task.FromResult(0);
    }

    private class NullDiscordNotifier : IDiscordNotifier
    {
        public Task<int> NotifyAsync(string title, string message, string? logPath, CancellationToken ct)
            => Task.FromResult(0);
    }

    // No-op manifest writer: swallows all writes (pipeline runs in test use --dry-run
    // and return before reaching any manifest write; this satisfies the constructor).
    private class NullManifestWriter : IManifestWriter
    {
        public void Write(PipelineRunManifest manifest) { }
    }

    // Records every manifest that was written so tests can assert on the final state.
    private class CapturingManifestWriter : IManifestWriter
    {
        public List<PipelineRunManifest> Written { get; } = [];
        public void Write(PipelineRunManifest manifest) => Written.Add(manifest);

        // Convenience: the last manifest written is the final state.
        public PipelineRunManifest? Last => Written.Count > 0 ? Written[^1] : null;
    }

    // No-op resume service: always returns null (no prior run to inherit from).
    private class NullRunResumeService : IRunResumeService
    {
        public PipelineRunManifest? FindCandidate(string target) => null;
        public PipelineRunManifest? LoadCandidate(string runId, out string? reason)
        {
            reason = null;
            return null;
        }
        public IReadOnlySet<string> GetInheritedInputPaths(PipelineRunManifest prior, string stepName)
            => new HashSet<string>();
    }

    // Simulates handbrake where every file fails (exit 1, no successes).
    private class FailingHandbrakeRunner : IHandbrakeRunner
    {
        public Task<int> RunAsync(
            string target, PipelineRun run, HandbrakeScriptOptions options,
            Action<StepFileProgress>? onProgress, ILogSink log, CancellationToken ct,
            IReadOnlySet<string>? inheritedFiles = null)
        {
            onProgress?.Invoke(new StepFileProgress
            {
                TotalFiles     = 1,
                ProcessedFiles = 1,
                FailedFiles    = 1,
                Files          = [new FileJobRecord
                {
                    InputPath  = "/incoming/movies/test.mkv",
                    OutputPath = "/staging/movies/test.norm.mp4",
                    Status     = StepStatus.Failed,
                    ExitCode   = 1
                }]
            });
            return Task.FromResult(1);
        }
    }

    // Simulates handbrake where one file succeeds and one fails (exit 1, partial success).
    private class PartiallyFailingHandbrakeRunner : IHandbrakeRunner
    {
        public Task<int> RunAsync(
            string target, PipelineRun run, HandbrakeScriptOptions options,
            Action<StepFileProgress>? onProgress, ILogSink log, CancellationToken ct,
            IReadOnlySet<string>? inheritedFiles = null)
        {
            onProgress?.Invoke(new StepFileProgress
            {
                TotalFiles     = 2,
                ProcessedFiles = 2,
                FailedFiles    = 1,
                Files          =
                [
                    new FileJobRecord
                    {
                        InputPath  = "/incoming/movies/good.mkv",
                        OutputPath = "/staging/movies/good.norm.mp4",
                        Status     = StepStatus.Complete,
                        ExitCode   = 0
                    },
                    new FileJobRecord
                    {
                        InputPath  = "/incoming/movies/bad.mkv",
                        OutputPath = "/staging/movies/bad.norm.mp4",
                        Status     = StepStatus.Failed,
                        ExitCode   = 1
                    }
                ]
            });
            return Task.FromResult(1);
        }
    }

    // Simulates a runner being interrupted mid-execution (e.g. Ctrl+C).
    // Calls ct.ThrowIfCancellationRequested() so a pre-cancelled token causes
    // an OperationCanceledException the same way a real runner would produce it.
    private class CancellingHandbrakeRunner : IHandbrakeRunner
    {
        public Task<int> RunAsync(
            string target, PipelineRun run, HandbrakeScriptOptions options,
            Action<StepFileProgress>? onProgress, ILogSink log, CancellationToken ct,
            IReadOnlySet<string>? inheritedFiles = null)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(0);
        }
    }

    // ── Factory helpers ──────────────────────────────────────────────────────

    private static PipelineCommandHandler MakeHandler() => new(
        new NullHandbrakeRunner(),
        new NullNormalizeRunner(),
        new NullPromoteRunner(),
        new NullDiscordNotifier(),
        new NullManifestWriter(),
        new NullRunResumeService());

    // Overload for cancellation tests: injects a cancelling runner and a writer
    // that captures every manifest snapshot so we can assert on the final status.
    private static PipelineCommandHandler MakeCancellingHandler(CapturingManifestWriter writer) => new(
        new CancellingHandbrakeRunner(),
        new NullNormalizeRunner(),
        new NullPromoteRunner(),
        new NullDiscordNotifier(),
        writer,
        new NullRunResumeService());

    private static PipelineCommandHandler MakeFailingHandbrakeHandler(CapturingManifestWriter writer) => new(
        new FailingHandbrakeRunner(),
        new NullNormalizeRunner(),
        new NullPromoteRunner(),
        new NullDiscordNotifier(),
        writer,
        new NullRunResumeService());

    private static PipelineCommandHandler MakePartiallyFailingHandbrakeHandler(CapturingManifestWriter writer) => new(
        new PartiallyFailingHandbrakeRunner(),
        new NullNormalizeRunner(),
        new NullPromoteRunner(),
        new NullDiscordNotifier(),
        writer,
        new NullRunResumeService());

    // LogDir uses the system temp directory so TeeLogSink can create log files
    // without depending on /logs existing on the development machine.
    private static PipelineOptions OptionsFor(string target, bool dryRun = true) =>
        new(
            Target:       target,
            IncomingRoot: "/incoming",
            StagingRoot:  "/staging",
            LibraryRoot:  "/library",
            RunId:        "030526143000",
            DryRun:       dryRun,
            Yes:          false,
            LogDir:       Path.GetTempPath(),
            Json:         false,
            Verbosity:    "normal",
            StopOnError:  true,
            Notify:       true,
            Step:         null,
            Until:        null,
            Resume:       false,
            ResumeFrom:   null
        );

    // ── Validation failure tests ─────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_TargetUnderWrongRoot_ReturnsExitCode2()
    {
        var rc = await MakeHandler().HandleAsync(OptionsFor("/staging/tv/My Show"), CancellationToken.None);

        rc.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_TargetIsIncomingRoot_ReturnsExitCode2()
    {
        var rc = await MakeHandler().HandleAsync(OptionsFor("/incoming"), CancellationToken.None);

        rc.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_TvTargetTooDeep_ReturnsExitCode2()
    {
        var rc = await MakeHandler().HandleAsync(OptionsFor("/incoming/tv/My Show/Season 1"), CancellationToken.None);

        rc.Should().Be(2);
    }

    // ── Cancellation tests ───────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_CancelledDuringStep_ReturnsExitCode130()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel so the first runner call immediately throws

        var writer  = new CapturingManifestWriter();
        var handler = MakeCancellingHandler(writer);
        // Yes: true skips the interactive confirmation prompt
        var options = OptionsFor("/incoming/tv/My Show", dryRun: false) with { Yes = true };

        var rc = await handler.HandleAsync(options, cts.Token);

        rc.Should().Be(130);
    }

    [Fact]
    public async Task HandleAsync_CancelledDuringStep_WritesManifestWithCancelledStatus()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var writer  = new CapturingManifestWriter();
        var handler = MakeCancellingHandler(writer);
        var options = OptionsFor("/incoming/tv/My Show", dryRun: false) with { Yes = true };

        await handler.HandleAsync(options, cts.Token);

        // The final manifest snapshot must reflect the cancelled run state.
        writer.Last.Should().NotBeNull();
        writer.Last!.Status.Should().Be(RunStatus.Cancelled);
        writer.Last!.ExitCode.Should().Be(130);
        writer.Last!.CompletedAt.Should().NotBeNull();
        writer.Last!.StepLogFiles.Should().ContainKey("handbrake");
        writer.Last!.StepLogFiles.Should().ContainKey("normalize");
        writer.Last!.StepLogFiles.Should().ContainKey("promote");
        writer.Last!.StepLogFiles.Should().NotContainKey("pipeline");
        writer.Last!.Steps.Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s.LogFile));
    }

    [Fact]
    public async Task HandleAsync_CancelledDuringStep_MarksActiveStepAsCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var writer  = new CapturingManifestWriter();
        var handler = MakeCancellingHandler(writer);
        var options = OptionsFor("/incoming/tv/My Show", dryRun: false) with { Yes = true };

        await handler.HandleAsync(options, cts.Token);

        // The step that was mid-run (handbrake) must be Cancelled, not stuck in Running.
        // Pending steps that never started should remain Pending.
        var steps = writer.Last!.Steps;
        steps.Should().Contain(s => s.Name == "handbrake" && s.Status == StepStatus.Cancelled);
        steps.Should().NotContain(s => s.Status == StepStatus.Running);
    }

    // ── StopOnError=false: all files fail → run is Failed ────────────────────

    [Fact]
    public async Task HandleAsync_AllFilesFailHandbrake_StopOnErrorFalse_ReturnsExitCode1()
    {
        var writer  = new CapturingManifestWriter();
        var handler = MakeFailingHandbrakeHandler(writer);
        var options = OptionsFor("/incoming/tv/My Show", dryRun: false) with { Yes = true, StopOnError = false };

        var rc = await handler.HandleAsync(options, CancellationToken.None);

        rc.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_AllFilesFailHandbrake_StopOnErrorFalse_WritesFailedManifest()
    {
        var writer  = new CapturingManifestWriter();
        var handler = MakeFailingHandbrakeHandler(writer);
        var options = OptionsFor("/incoming/tv/My Show", dryRun: false) with { Yes = true, StopOnError = false };

        await handler.HandleAsync(options, CancellationToken.None);

        // No files succeeded, so run-level status must be Failed.
        writer.Last.Should().NotBeNull();
        writer.Last!.Status.Should().Be(RunStatus.Failed);
        writer.Last!.ExitCode.Should().Be(1);
        writer.Last!.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAsync_AllFilesFailHandbrake_StopOnErrorFalse_CompletesAllSteps()
    {
        var writer  = new CapturingManifestWriter();
        var handler = MakeFailingHandbrakeHandler(writer);
        var options = OptionsFor("/incoming/tv/My Show", dryRun: false) with { Yes = true, StopOnError = false };

        await handler.HandleAsync(options, CancellationToken.None);

        // normalize and promote must have been attempted (not left Pending).
        var steps = writer.Last!.Steps;
        steps.Should().Contain(s => s.Name == "handbrake"  && s.Status == StepStatus.Failed);
        steps.Should().Contain(s => s.Name == "normalize"  && s.Status == StepStatus.Complete);
        steps.Should().Contain(s => s.Name == "promote"    && s.Status == StepStatus.Complete);
    }

    // ── StopOnError=false: some files fail → run is Complete ─────────────────

    [Fact]
    public async Task HandleAsync_SomeFilesFailHandbrake_StopOnErrorFalse_ReturnsZero()
    {
        var writer  = new CapturingManifestWriter();
        var handler = MakePartiallyFailingHandbrakeHandler(writer);
        var options = OptionsFor("/incoming/tv/My Show", dryRun: false) with { Yes = true, StopOnError = false };

        var rc = await handler.HandleAsync(options, CancellationToken.None);

        rc.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_SomeFilesFailHandbrake_StopOnErrorFalse_WritesCompleteManifest()
    {
        var writer  = new CapturingManifestWriter();
        var handler = MakePartiallyFailingHandbrakeHandler(writer);
        var options = OptionsFor("/incoming/tv/My Show", dryRun: false) with { Yes = true, StopOnError = false };

        await handler.HandleAsync(options, CancellationToken.None);

        // At least one file succeeded, so the run is considered complete.
        writer.Last.Should().NotBeNull();
        writer.Last!.Status.Should().Be(RunStatus.Complete);
        writer.Last!.ExitCode.Should().Be(0);
        writer.Last!.CompletedAt.Should().NotBeNull();
        // The handbrake step itself still reflects that it had failures.
        writer.Last!.Steps.Should().Contain(s => s.Name == "handbrake" && s.Status == StepStatus.Failed);
    }

    // ── StopOnError=true: halt immediately on handbrake failures ─────────────

    [Fact]
    public async Task HandleAsync_HandbrakeFailures_StopOnErrorTrue_HaltsPipeline()
    {
        var writer  = new CapturingManifestWriter();
        var handler = MakeFailingHandbrakeHandler(writer);
        var options = OptionsFor("/incoming/tv/My Show", dryRun: false) with { Yes = true, StopOnError = true };

        var rc = await handler.HandleAsync(options, CancellationToken.None);

        rc.Should().Be(1);
        // normalize and promote must remain Pending (never started)
        var steps = writer.Last!.Steps;
        steps.Should().Contain(s => s.Name == "normalize" && s.Status == StepStatus.Pending);
        steps.Should().Contain(s => s.Name == "promote"   && s.Status == StepStatus.Pending);
    }

    // ── Valid target / dry-run tests ─────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_ValidTvTarget_DryRun_ReturnsZero()
    {
        var rc = await MakeHandler().HandleAsync(OptionsFor("/incoming/tv/My Show"), CancellationToken.None);

        rc.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_ValidMovieFileTarget_DryRun_ReturnsZero()
    {
        var rc = await MakeHandler().HandleAsync(OptionsFor("/incoming/movies/Alien.mkv"), CancellationToken.None);

        rc.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_ValidMoviesDirTarget_DryRun_ReturnsZero()
    {
        var rc = await MakeHandler().HandleAsync(OptionsFor("/incoming/movies"), CancellationToken.None);

        rc.Should().Be(0);
    }
}
