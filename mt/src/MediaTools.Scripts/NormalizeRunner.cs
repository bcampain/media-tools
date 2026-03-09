using System.Diagnostics;
using MediaTools.Domain.Models;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.Scripts;

// Launches /bin/normalize_audio as a child process,
// streams its stdout/stderr to the ILogSink, and returns the exit code.
public class NormalizeRunner(ILogSink log) : INormalizeRunner
{
    private const string ScriptPath = "/usr/local/bin/normalize_audio";

    public async Task<int> RunAsync(PipelineRun run, NormalizeScriptOptions options, CancellationToken ct)
    {
        var args = BuildArgs(run, options);
        log.Info($"[normalize] Invoking: {ScriptPath} {args}");

        var psi = new ProcessStartInfo(ScriptPath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            WorkingDirectory       = "/"
        };

        using var process = new Process { StartInfo = psi };

        // Both streams go directly to the console — CLI tools commonly use stderr
        // for diagnostic/progress info rather than just errors, so there's no
        // reliable way to filter "real errors" from the stream. Only the C# handler's
        // explicit log calls (above/below this block) end up in the log file.
        process.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };

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

    private static string BuildArgs(PipelineRun run, NormalizeScriptOptions options)
    {
        var parts = new List<string>
        {
            $"\"{run.Target}\"",
            $"--run-id {run.RunId}",
            $"--target-i {options.TargetI}",
            $"--true-peak {options.TruePeak}",
            $"--lra {options.Lra}",
            $"--stereo-track {options.StereoTrack}"
        };
        if (options.OnePass) parts.Add("--one-pass");
        if (options.Force)   parts.Add("--force");
        if (options.DryRun)  parts.Add("--dry-run");
        return string.Join(" ", parts);
    }
}