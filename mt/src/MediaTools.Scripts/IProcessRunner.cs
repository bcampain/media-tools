using MediaTools.Domain.Models;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.Scripts;

public interface IProcessRunner<TOptions>
{
    Task<int> RunAsync(string target, PipelineRun run, TOptions options, ILogSink log, CancellationToken ct);
}
