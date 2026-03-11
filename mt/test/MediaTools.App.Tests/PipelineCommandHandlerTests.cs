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
            Action<StepFileProgress>? onProgress, CancellationToken ct)
            => Task.FromResult(0);
    }

    private class NullNormalizeRunner : INormalizeRunner
    {
        public Task<int> RunAsync(string target, PipelineRun run, NormalizeScriptOptions options, CancellationToken ct)
            => Task.FromResult(0);
    }

    private class NullPromoteRunner : IPromoteRunner
    {
        public Task<int> RunAsync(string target, PipelineRun run, PromoteScriptOptions options, CancellationToken ct)
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

    // ── Factory helpers ──────────────────────────────────────────────────────

    private static PipelineCommandHandler MakeHandler() => new(
        new NullHandbrakeRunner(),
        new NullNormalizeRunner(),
        new NullPromoteRunner(),
        new NullDiscordNotifier(),
        new ConsoleLogSink(),
        new NullManifestWriter());

    private static PipelineOptions OptionsFor(string target, bool dryRun = true) =>
        new(
            Target:       target,
            IncomingRoot: "/incoming",
            StagingRoot:  "/staging",
            LibraryRoot:  "/library",
            RunId:        "030526143000",
            DryRun:       dryRun,
            Yes:          false,
            LogDir:       "/logs",
            Json:         false,
            Verbosity:    "normal",
            StopOnError:  true,
            Notify:       true,
            Step:         null,
            Until:        null
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
