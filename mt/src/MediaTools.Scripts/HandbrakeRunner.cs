using System.Diagnostics;
using MediaTools.Domain.Models;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.Scripts;

// The real IHandbrakeRunner: launches /bin/handbrake_mp4 as a child process,
// streams its stdout/stderr to the ILogSink, and returns the exit code.
public class HandbrakeRunner(ILogSink log) : IHandbrakeRunner
{
    // Script path inside the Docker container
    private const string ScriptPath = "/bin/handbrake_mp4";

    public async Task<int> RunAsync(PipelineRun run, HandbrakeScriptOptions options, CancellationToken ct)
    {
        var args = BuildArgs(run, options);
        log.Info($"[handbrake] Invoking: {ScriptPath} {args}");

        var psi = new ProcessStartInfo(ScriptPath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = new Process { StartInfo = psi };

        // Forward the subprocess output lines to our log sink in real time
        process.OutputDataReceived += (_, e) => { if (e.Data != null) log.Info(e.Data); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data != null) log.Warn(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Kill the child process tree so it doesn't keep running after cancellation.
            process.Kill(entireProcessTree: true);
            throw;
        }

        // WaitForExitAsync returns when the process exits, but async output callbacks
        // may still be in-flight. The synchronous WaitForExit() ensures all buffered
        // output has been flushed through the log sink before we return.
        process.WaitForExit();
        return process.ExitCode;
    }

    private static string BuildArgs(PipelineRun run, HandbrakeScriptOptions options)
    {
        var parts = new List<string>
        {
            $"\"{run.Target}\"",
            $"--run-id {run.RunId}",
            $"--quality {options.Quality}",
            $"--preset {options.Preset}",
            $"--max-depth {options.MaxDepth}"
        };
        if (options.Force)  parts.Add("--force");
        if (options.DryRun) parts.Add("--dry-run");
        return string.Join(" ", parts);
    }
}