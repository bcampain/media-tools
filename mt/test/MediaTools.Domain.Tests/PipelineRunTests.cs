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


}
