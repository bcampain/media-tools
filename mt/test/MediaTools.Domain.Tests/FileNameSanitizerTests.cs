using FluentAssertions;
using MediaTools.Domain.FileMapping;

namespace MediaTools.Domain.Tests;

/// <summary>
/// Tests for FileNameSanitizer.RemoveCommas — the pure rule that strips commas from
/// incoming filenames (HandBrake's --srt-file can't handle them; see the class docs).
///
/// A table-driven [Theory] fits well here: each row is one naming edge case, and
/// adding a newly discovered weird filename later is a one-line change.
/// </summary>
public class FileNameSanitizerTests
{
    [Theory]
    // Typical case: ", " collapses to a single space
    [InlineData("S01E01 - Hello, World.mkv",   "S01E01 - Hello World.mkv")]
    // No space after the comma: still separated by one space, not glued together
    [InlineData("a,b.mkv",                     "a b.mkv")]
    // Spaces on both sides: no double space left behind
    [InlineData("a , b.mkv",                   "a b.mkv")]
    // Consecutive commas, with or without spaces between them, become one space
    [InlineData("x,,y.mkv",                    "x y.mkv")]
    [InlineData("x, ,y.mkv",                   "x y.mkv")]
    // Trailing comma on the stem: no stray space before the extension
    [InlineData("Show,.mkv",                   "Show.mkv")]
    // Dotted scene-style names: dots and extension are preserved
    [InlineData("Show.S01E01.1080p, WEB.mkv",  "Show.S01E01.1080p WEB.mkv")]
    // Works for subtitle filenames too, including the .en tag
    [InlineData("Hello, World.en.srt",         "Hello World.en.srt")]
    public void RemoveCommas_CollapsesCommasAndSurroundingSpaces(string input, string expected)
    {
        FileNameSanitizer.RemoveCommas(input).Should().Be(expected);
    }

    [Fact]
    public void RemoveCommas_NoCommas_ReturnsInputUnchanged()
    {
        FileNameSanitizer.RemoveCommas("No Commas Here.mkv").Should().Be("No Commas Here.mkv");
    }
}
