namespace MediaTools.Infrastructure.Logging;

// Decorator pattern: wraps an inner ILogSink and simultaneously appends each
// message to a file. Named after the Unix `tee` command which splits a stream
// to both stdout and a file at the same time.
//
// This keeps ConsoleLogSink focused on one job, and lets us add file logging
// purely at the composition root (Program.cs) without touching any handlers.
public class TeeLogSink(ILogSink inner, string logFilePath) : ILogSink
{
    public void Info(string message)  { inner.Info(message);  Append($"[INFO]  {message}"); }
    public void Warn(string message)  { inner.Warn(message);  Append($"[WARN]  {message}"); }
    public void Error(string message) { inner.Error(message); Append($"[ERROR] {message}"); }

    private void Append(string line) =>
        File.AppendAllText(logFilePath, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
}
