using System.Diagnostics;
using MediaTools.Infrastructure.Logging;

namespace MediaTools.Infrastructure.Notifications;

// Launches /bin/notify_discord as a child process,
// streams its stdout/stderr to the ILogSink, and returns the exit code.
// The script reads DISCORD_WEBHOOK_URL from the environment (inherited from parent).
// This is the script-backed implementation; it will be replaced with a direct
// HttpClient POST to the Discord webhook URL in a future milestone.
public class DiscordNotifier(ILogSink log) : IDiscordNotifier
{
    private const string ScriptPath = "/usr/local/bin/notify_discord";

    public async Task<int> NotifyAsync(string title, string message, string? logPath, CancellationToken ct)
    {
        var args = BuildArgs(title, message, logPath);
        log.Info($"[notify-discord] Invoking: {ScriptPath} {args}");

        var psi = new ProcessStartInfo(ScriptPath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            WorkingDirectory       = "/"
        };

        using var process = new Process { StartInfo = psi };

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
            process.Kill(entireProcessTree: true);
            throw;
        }

        // WaitForExitAsync returns when the process exits, but async output callbacks
        // may still be in-flight. The synchronous WaitForExit() ensures all buffered
        // output has been flushed through the log sink before we return.
        process.WaitForExit();
        return process.ExitCode;
    }

    private static string BuildArgs(string title, string message, string? logPath)
    {
        // Script signature: notify_discord "title" "message" ["logfile"]
        // All positional — no --flags.
        var parts = new List<string>
        {
            $"\"{title}\"",
            $"\"{message}\""
        };
        if (logPath != null) parts.Add($"\"{logPath}\"");
        return string.Join(" ", parts);
    }
}
