using MediaTools.Domain.Models;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.Scripts.Stubs;

public class StubNormalizeRunner(ILogSink log) : INormalizeRunner
{
    public Task<int> RunAsync(PipelineRun run, NormalizeScriptOptions options, CancellationToken ct)
    {
        log.Info($"[stub] normalize_audio {run.Target} --run-id {run.RunId} (not implemented)");
        return Task.FromResult(0);
    }
}
