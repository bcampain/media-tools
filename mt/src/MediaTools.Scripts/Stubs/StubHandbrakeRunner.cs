using MediaTools.Domain.Models;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.Scripts.Stubs;

public class StubHandbrakeRunner(ILogSink log) : IHandbrakeRunner
{
    public Task<int> RunAsync(PipelineRun run, HandbrakeScriptOptions options, CancellationToken ct)
    {
        log.Info($"[stub] handbrake_mp4 {run.Target} --run-id {run.RunId} (not implemented)");
        return Task.FromResult(0);
    }
}
