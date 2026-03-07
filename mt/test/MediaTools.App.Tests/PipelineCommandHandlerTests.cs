using FluentAssertions;
using MediaTools.App.Handlers;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.App.Tests;

/// <summary>
/// Tests for PipelineCommandHandler.
///
/// We can test validation failure paths without any mocking because they return
/// before reaching the confirmation prompt or runner stubs. The confirmation prompt
/// (Console.ReadLine) is not exercised here — that path requires manual testing.
/// All valid-target tests use --dry-run so they also exit before the prompt.
/// </summary>
public class PipelineCommandHandlerTests
{
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

    [Fact]
    public async Task HandleAsync_TargetUnderWrongRoot_ReturnsExitCode2()
    {
        var handler = new PipelineCommandHandler(new ConsoleLogSink());

        var rc = await handler.HandleAsync(OptionsFor("/staging/tv/My Show"), CancellationToken.None);

        rc.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_TargetIsIncomingRoot_ReturnsExitCode2()
    {
        var handler = new PipelineCommandHandler(new ConsoleLogSink());

        var rc = await handler.HandleAsync(OptionsFor("/incoming"), CancellationToken.None);

        rc.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_TvTargetTooDeep_ReturnsExitCode2()
    {
        var handler = new PipelineCommandHandler(new ConsoleLogSink());

        var rc = await handler.HandleAsync(OptionsFor("/incoming/tv/My Show/Season 1"), CancellationToken.None);

        rc.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_ValidTvTarget_DryRun_ReturnsZero()
    {
        var handler = new PipelineCommandHandler(new ConsoleLogSink());

        var rc = await handler.HandleAsync(OptionsFor("/incoming/tv/My Show"), CancellationToken.None);

        rc.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_ValidMovieFileTarget_DryRun_ReturnsZero()
    {
        var handler = new PipelineCommandHandler(new ConsoleLogSink());

        var rc = await handler.HandleAsync(OptionsFor("/incoming/movies/Alien.mkv"), CancellationToken.None);

        rc.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_ValidMoviesDirTarget_DryRun_ReturnsZero()
    {
        var handler = new PipelineCommandHandler(new ConsoleLogSink());

        var rc = await handler.HandleAsync(OptionsFor("/incoming/movies"), CancellationToken.None);

        rc.Should().Be(0);
    }
}
