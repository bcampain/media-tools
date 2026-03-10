namespace MediaTools.Infrastructure.Logging;

public class ConsoleLogSink : ILogSink
{
    public void Info(string message)  => Console.WriteLine($"[INFO]  {message}");
    public void Warn(string message)  => Console.WriteLine($"[WARN]  {message}");
    public void Error(string message) => Console.Error.WriteLine($"[ERROR] {message}");
}
