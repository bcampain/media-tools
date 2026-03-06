using FluentAssertions;
using MediaTools.Domain.Models;

namespace MediaTools.Domain.Tests;

public class PipelineRunTests
{
    [Fact]
    public void GenerateRunId_HasCorrectFormat()
    {
        var runId = PipelineRun.GenerateRunId();

        runId.Should().HaveLength(12);
        runId.Should().MatchRegex(@"^\d{12}$");
    }

    [Fact]
    public void LogFilePath_ForTv_UsesShowName()
    {
        var run = new PipelineRun(
            RunId:        "030526143000",
            StartedAt:    DateTime.UtcNow,
            Target:       "/incoming/tv/My Show",
            TargetMode:   TargetMode.Dir,
            Kind:         Kind.Tv,
            StagingRoot:  "/staging",
            LibraryRoot:  "/library",
            IncomingRoot: "/incoming",
            LogDir:       "/logs"
        );

        run.LogFilePath().Should().Be("/logs/My Show-media-tools-030526143000.log");
    }

    [Fact]
    public void LogFilePath_ForMovies_UsesKindName()
    {
        var run = new PipelineRun(
            RunId:        "030526143000",
            StartedAt:    DateTime.UtcNow,
            Target:       "/incoming/movies/Alien.mp4",
            TargetMode:   TargetMode.File,
            Kind:         Kind.Movies,
            StagingRoot:  "/staging",
            LibraryRoot:  "/library",
            IncomingRoot: "/incoming",
            LogDir:       "/logs"
        );

        run.LogFilePath().Should().Be("/logs/movies-media-tools-030526143000.log");
    }

    [Fact]
    public void LogFilePath_ForClips_UsesKindName()
    {
        var run = new PipelineRun(
            RunId:        "030526143000",
            StartedAt:    DateTime.UtcNow,
            Target:       "/incoming/clips/",
            TargetMode:   TargetMode.Dir,
            Kind:         Kind.Clips,
            StagingRoot:  "/staging",
            LibraryRoot:  "/library",
            IncomingRoot: "/incoming",
            LogDir:       "/logs"
        );

        run.LogFilePath().Should().Be("/logs/clips-media-tools-030526143000.log");
    }
}