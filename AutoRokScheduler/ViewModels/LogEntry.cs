using System;

namespace AutoRokScheduler.ViewModels;

public enum LogLevel { Info, Success, Warn, Error }

public sealed class LogEntry
{
    public DateTime Time { get; init; } = DateTime.Now;
    public string Message { get; init; } = "";
    public LogLevel Level { get; init; } = LogLevel.Info;

    public string TimeText => Time.ToString("HH:mm:ss");
}
