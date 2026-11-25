using FluentAssertions;
using MediaTools.Domain.FileMapping;

namespace MediaTools.Domain.Tests;

/// <summary>
/// Tests for VideoPathMapper — pure path-mapping helpers shared by the step runners.
///
/// These use [Theory] + [InlineData]: xUnit runs the same test method once per
/// InlineData row, passing the values as arguments. It's the idiomatic way to cover
/// many input→output cases of a pure function without copy-pasting [Fact]s.
/// Each row shows up as its own test case in the runner output.
/// </summary>
public class VideoPathMapperTests
{
    // -------------------------------------------------------------------------
    // ExternalSubtitleCandidates — <video dir>/Subs/<stem>[.en].srt convention
    // -------------------------------------------------------------------------

    [Theory]
    // TV episode inside a season folder
    [InlineData("/incoming/tv/My Show/Season 1/My Show - S01E03.mkv",
                "/incoming/tv/My Show/Season 1/Subs/My Show - S01E03")]
    // TV episode directly under the show root (no season folder)
    [InlineData("/incoming/tv/My Show/S01E01.mp4",
                "/incoming/tv/My Show/Subs/S01E01")]
    // Movie directly under the kind root
    [InlineData("/incoming/movies/Alien.mkv",
                "/incoming/movies/Subs/Alien")]
    // Dotted scene-style name: only the LAST extension is replaced
    [InlineData("/incoming/tv/Show/Season 2/Show.Name.S02E05.1080p.WEB.m4v",
                "/incoming/tv/Show/Season 2/Subs/Show.Name.S02E05.1080p.WEB")]
    public void ExternalSubtitleCandidates_ReturnsUntaggedThenEnglishTaggedSrt(string input, string expectedStemPath)
    {
        // Equal() checks exact contents AND order: exactly these two, untagged first
        // (the runner picks the first file that exists). Because it's exact, it also
        // guarantees no other language (e.g. ".es.srt") is ever a candidate.
        VideoPathMapper.ExternalSubtitleCandidates(input)
            .Should().Equal(expectedStemPath + ".srt", expectedStemPath + ".en.srt");
    }

    // -------------------------------------------------------------------------
    // MapHandbrakeOutput — step 1 → step 2 .norm.mp4 handoff contract
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("/incoming/movies/Alien.mkv",
                "/staging/movies/Alien.norm.mp4")]
    [InlineData("/incoming/tv/Breaking Bad/Season 1/S01E01.mkv",
                "/staging/tv/Breaking Bad/Season 1/S01E01.norm.mp4")]
    [InlineData("/incoming/clips/funny/clip.mp4",
                "/staging/clips/funny/clip.norm.mp4")]
    public void MapHandbrakeOutput_MirrorsPathUnderStagingWithNormMp4(string input, string expected)
    {
        VideoPathMapper.MapHandbrakeOutput(input, "/incoming", "/staging").Should().Be(expected);
    }

    [Fact]
    public void MapHandbrakeOutput_TrailingSlashOnRoots_IsIgnored()
    {
        VideoPathMapper.MapHandbrakeOutput("/incoming/movies/Alien.mkv", "/incoming/", "/staging/")
            .Should().Be("/staging/movies/Alien.norm.mp4");
    }
}
