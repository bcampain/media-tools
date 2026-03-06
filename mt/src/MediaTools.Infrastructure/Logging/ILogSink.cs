namespace MediaTools.Infrastructure.Logging;

public interface ILogSink
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}
