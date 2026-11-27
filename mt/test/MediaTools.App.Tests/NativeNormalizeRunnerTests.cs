using FluentAssertions;
using MediaTools.App.Normalize;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.App.Tests;

/// <summary>
/// Unit tests for the pure (no I/O) helpers on NativeNormalizeRunner.
///
/// These helpers are `internal`, made visible to this project via
/// <c>&lt;InternalsVisibleTo Include="MediaTools.App.Tests" /&gt;</c> in MediaTools.App.csproj.
///
/// Pattern note: [Theory] + [InlineData] runs one test method once per data row,
/// which keeps "same assertion, many inputs" cases compact. Each row shows up as
/// its own test in the runner output, so a failure tells you exactly which input broke.
/// </summary>
public class NativeNormalizeRunnerTests
{
    // ── BuildInputOptions ────────────────────────────────────────────────────

    [Theory]
    [InlineData("dts")]
    [InlineData("DTS")]
    public void BuildInputOptions_Dts_ReturnsCoreOnly(string codec)
    {
        NativeNormalizeRunner.BuildInputOptions(codec).Should().Be("-core_only 1 ");
    }

    [Theory]
    [InlineData("ac3")]
    [InlineData("aac")]
    [InlineData("eac3")]
    [InlineData("truehd")]
    [InlineData(null)]
    public void BuildInputOptions_NonDts_ReturnsEmpty(string? codec)
    {
        // Non-DTS inputs must keep exactly the same command line as before.
        NativeNormalizeRunner.BuildInputOptions(codec).Should().BeEmpty();
    }

    // ── ParseAudioProbe ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("codec_name=dts\nchannels=6\n")]
    [InlineData("channels=6\ncodec_name=dts\n")]       // ffprobe's order isn't guaranteed
    [InlineData("codec_name=dts\r\nchannels=6\r\n")]   // tolerate CRLF
    public void ParseAudioProbe_ParsesByKey_RegardlessOfOrder(string output)
    {
        var (channels, codec) = NativeNormalizeRunner.ParseAudioProbe(output);

        channels.Should().Be(6);
        codec.Should().Be("dts");
    }

    [Fact]
    public void ParseAudioProbe_MissingChannels_ReturnsNullChannels()
    {
        // The caller treats null channels as a probe failure (existing behaviour).
        var (channels, codec) = NativeNormalizeRunner.ParseAudioProbe("codec_name=aac\n");

        channels.Should().BeNull();
        codec.Should().Be("aac");
    }

    [Fact]
    public void ParseAudioProbe_EmptyOutput_ReturnsNulls()
    {
        // e.g. a file with no audio stream: ffprobe prints nothing for a:0.
        var (channels, codec) = NativeNormalizeRunner.ParseAudioProbe("");

        channels.Should().BeNull();
        codec.Should().BeNull();
    }

    // ── IsFfmpegProgressLine ─────────────────────────────────────────────────

    [Theory]
    [InlineData("frame= 1234 fps=250 q=-1.0 size=  10240kB time=00:00:51.20 bitrate=1638.4kbits/s speed=10x")]
    [InlineData("size=    2048kB time=00:01:00.00 bitrate= 279.6kbits/s speed=40x")]
    [InlineData("   size=    2048kB time=00:01:00.00")]
    public void IsFfmpegProgressLine_ProgressLines_ReturnsTrue(string line)
    {
        NativeNormalizeRunner.IsFfmpegProgressLine(line).Should().BeTrue();
    }

    [Theory]
    [InlineData("[aac @ 0x55d0] Input contains (near) NaN/+-Inf")]
    [InlineData("Error submitting audio frame to the encoder")]
    [InlineData("Conversion failed!")]
    public void IsFfmpegProgressLine_ErrorLines_ReturnsFalse(string line)
    {
        NativeNormalizeRunner.IsFfmpegProgressLine(line).Should().BeFalse();
    }

    // ── StderrTail ───────────────────────────────────────────────────────────

    // A hand-rolled "spy" test double: records what was logged so tests can assert on it.
    // Same no-mocking-library approach as the null stubs in PipelineCommandHandlerTests.
    private sealed class RecordingLogSink : ILogSink
    {
        public List<string> Errors { get; } = [];
        public void Info(string message)  { }
        public void Warn(string message)  { }
        public void Error(string message) => Errors.Add(message);
    }

    [Fact]
    public void StderrTail_OverCapacity_KeepsOnlyNewestLines()
    {
        var tail  = new NativeNormalizeRunner.StderrTail();
        var total = NativeNormalizeRunner.StderrTailLines + 10;

        for (var i = 1; i <= total; i++)
            tail.Add($"line {i}");

        tail.Lines.Should().HaveCount(NativeNormalizeRunner.StderrTailLines);
        tail.Lines.First().Should().Be("line 11");          // oldest 10 were dropped
        tail.Lines.Last().Should().Be($"line {total}");     // newest is kept
    }

    [Fact]
    public void StderrTail_SkipsProgressLines_KeepsErrors()
    {
        var tail = new NativeNormalizeRunner.StderrTail();

        tail.Add("size=    2048kB time=00:01:00.00 bitrate= 279.6kbits/s");
        tail.Add("[aac @ 0x55d0] Input contains (near) NaN/+-Inf");
        tail.Add("frame= 1234 fps=250 q=-1.0");

        tail.Lines.Should().Equal("[aac @ 0x55d0] Input contains (near) NaN/+-Inf");
    }

    [Fact]
    public void StderrTail_LogTo_WritesHeaderThenLines()
    {
        var tail = new NativeNormalizeRunner.StderrTail();
        tail.Add("Conversion failed!");
        var log = new RecordingLogSink();

        tail.LogTo(log);

        log.Errors.Should().Equal(
            "[normalize]   ffmpeg stderr (tail):",
            "[normalize]     Conversion failed!");
    }

    [Fact]
    public void StderrTail_LogTo_Empty_WritesNothing()
    {
        // No stray header when ffmpeg failed without printing anything useful.
        var log = new RecordingLogSink();

        new NativeNormalizeRunner.StderrTail().LogTo(log);

        log.Errors.Should().BeEmpty();
    }
}
