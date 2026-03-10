using FluentAssertions;
using MediaTools.Domain.Models;
using MediaTools.Domain.Validation;

namespace MediaTools.Domain.Tests;

/// <summary>
/// Tests for TargetValidator — the pure domain logic that validates target paths
/// and infers Kind/TargetMode from path structure.
///
/// Because TargetValidator is a pure static class (no I/O, no dependencies),
/// every test here is a simple input→output assertion with no mocking needed.
/// This is one of the key benefits of keeping validation in the domain layer.
/// </summary>
public class TargetValidatorTests
{
    // -------------------------------------------------------------------------
    // Happy paths — ValidateIncoming
    // -------------------------------------------------------------------------

    [Fact]
    public void ValidateIncoming_TvShowDir_ReturnsKindTvAndDirMode()
    {
        var result = TargetValidator.ValidateIncoming("/incoming/tv/My Favorite Show", "/incoming");

        result.IsSuccess.Should().BeTrue();
        result.Target!.Kind.Should().Be(Kind.Tv);
        result.Target.Mode.Should().Be(TargetMode.Dir);
        result.Target.AbsolutePath.Should().Be("/incoming/tv/My Favorite Show");
    }

    [Fact]
    public void ValidateIncoming_MoviesDir_ReturnsKindMoviesAndDirMode()
    {
        var result = TargetValidator.ValidateIncoming("/incoming/movies", "/incoming");

        result.IsSuccess.Should().BeTrue();
        result.Target!.Kind.Should().Be(Kind.Movies);
        result.Target.Mode.Should().Be(TargetMode.Dir);
    }

    [Fact]
    public void ValidateIncoming_MoviesSubdir_ReturnsKindMoviesAndDirMode()
    {
        var result = TargetValidator.ValidateIncoming("/incoming/movies/Action", "/incoming");

        result.IsSuccess.Should().BeTrue();
        result.Target!.Kind.Should().Be(Kind.Movies);
        result.Target.Mode.Should().Be(TargetMode.Dir);
    }

    [Fact]
    public void ValidateIncoming_MovieFile_ReturnsFileModeAndMoviesKind()
    {
        var result = TargetValidator.ValidateIncoming("/incoming/movies/Alien.mkv", "/incoming");

        result.IsSuccess.Should().BeTrue();
        result.Target!.Kind.Should().Be(Kind.Movies);
        result.Target.Mode.Should().Be(TargetMode.File);
    }

    [Theory]
    [InlineData(".mkv")]
    [InlineData(".mp4")]
    [InlineData(".m4v")]
    public void ValidateIncoming_VideoFileExtensions_AreRecognizedAsFileMode(string ext)
    {
        var result = TargetValidator.ValidateIncoming($"/incoming/movies/Movie{ext}", "/incoming");

        result.IsSuccess.Should().BeTrue();
        result.Target!.Mode.Should().Be(TargetMode.File);
    }

    [Fact]
    public void ValidateIncoming_ClipsDir_ReturnsKindClips()
    {
        var result = TargetValidator.ValidateIncoming("/incoming/clips", "/incoming");

        result.IsSuccess.Should().BeTrue();
        result.Target!.Kind.Should().Be(Kind.Clips);
        result.Target.Mode.Should().Be(TargetMode.Dir);
    }

    [Fact]
    public void ValidateIncoming_PathWithTrailingSlash_IsNormalized()
    {
        var result = TargetValidator.ValidateIncoming("/incoming/tv/My Show/", "/incoming");

        result.IsSuccess.Should().BeTrue();
        result.Target!.AbsolutePath.Should().Be("/incoming/tv/My Show");
    }

    // -------------------------------------------------------------------------
    // Happy paths — ValidateStaging
    // -------------------------------------------------------------------------

    [Fact]
    public void ValidateStaging_TvShowDir_IsValid()
    {
        var result = TargetValidator.ValidateStaging("/staging/tv/My Show", "/staging");

        result.IsSuccess.Should().BeTrue();
        result.Target!.Kind.Should().Be(Kind.Tv);
        result.Target.Mode.Should().Be(TargetMode.Dir);
    }

    [Fact]
    public void ValidateStaging_MoviesFile_IsValid()
    {
        var result = TargetValidator.ValidateStaging("/staging/movies/Alien.mp4", "/staging");

        result.IsSuccess.Should().BeTrue();
        result.Target!.Kind.Should().Be(Kind.Movies);
        result.Target.Mode.Should().Be(TargetMode.File);
    }

    // -------------------------------------------------------------------------
    // Validation failures — root scoping
    // -------------------------------------------------------------------------

    [Fact]
    public void ValidateIncoming_TargetIsRoot_Fails()
    {
        var result = TargetValidator.ValidateIncoming("/incoming", "/incoming");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("root itself");
    }

    [Fact]
    public void ValidateIncoming_TargetOutsideRoot_Fails()
    {
        var result = TargetValidator.ValidateIncoming("/staging/tv/My Show", "/incoming");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("must be under");
    }

    [Fact]
    public void ValidateIncoming_PathTraversal_Fails()
    {
        // Path.GetFullPath resolves ".." before the StartsWith check,
        // so "/incoming/../staging/tv/My Show" → "/staging/tv/My Show" which fails the root check.
        var result = TargetValidator.ValidateIncoming("/incoming/../staging/tv/My Show", "/incoming");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("must be under");
    }

    // -------------------------------------------------------------------------
    // Validation failures — kind segment
    // -------------------------------------------------------------------------

    [Fact]
    public void ValidateIncoming_UnknownKindSegment_Fails()
    {
        var result = TargetValidator.ValidateIncoming("/incoming/downloads/something", "/incoming");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("tv", Exactly.Once());
    }

    // -------------------------------------------------------------------------
    // Validation failures — TV shape rules
    // -------------------------------------------------------------------------

    [Fact]
    public void ValidateIncoming_TvKindOnly_NoShowName_Fails()
    {
        var result = TargetValidator.ValidateIncoming("/incoming/tv", "/incoming");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("show name");
    }

    [Fact]
    public void ValidateIncoming_TvTooDeep_Fails()
    {
        var result = TargetValidator.ValidateIncoming("/incoming/tv/My Show/Season 1", "/incoming");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("one level deep");
    }

    // -------------------------------------------------------------------------
    // DeriveHandoffTarget
    // -------------------------------------------------------------------------

    [Fact]
    public void DeriveHandoffTarget_TvShow_SwapsRoot()
    {
        var validated = new ValidatedTarget(Kind.Tv, TargetMode.Dir, "/incoming/tv/My Show");
        var handoff = TargetValidator.DeriveHandoffTarget(
            validated, "/incoming", "/staging");

        handoff.Should().Be("/staging/tv/My Show");
    }

    [Fact]
    public void DeriveHandoffTarget_MoviesDir_SwapsRoot()
    {
        var validated = new ValidatedTarget(Kind.Movies, TargetMode.Dir, "/incoming/movies");
        var handoff = TargetValidator.DeriveHandoffTarget(
            validated, "/incoming", "/staging");

        handoff.Should().Be("/staging/movies");
    }

    [Fact]
    public void DeriveHandoffTarget_MovieFile_ReturnsKindDirNotFilePath()
    {
        // Single-file targets hand off to the staging kind dir, not the file path,
        // because the output filename ({stem}.norm.mp4) isn't known until handbrake runs.
        var validated = new ValidatedTarget(Kind.Movies, TargetMode.File, "/incoming/movies/Alien.mkv");
        var handoff = TargetValidator.DeriveHandoffTarget(
            validated, "/incoming", "/staging");

        handoff.Should().Be("/staging/movies");
    }

    [Fact]
    public void DeriveHandoffTarget_ClipsFile_ReturnsClipsKindDir()
    {
        var validated = new ValidatedTarget(Kind.Clips, TargetMode.File, "/incoming/clips/clip.mkv");
        var handoff = TargetValidator.DeriveHandoffTarget(
            validated, "/incoming", "/staging");

        handoff.Should().Be("/staging/clips");
    }
}
