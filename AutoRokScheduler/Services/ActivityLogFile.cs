using System;
using System.IO;
using AutoRokScheduler.ViewModels;

namespace AutoRokScheduler.Services;

/// <summary>
/// Mirrors the in-app LOG panel to activity.log.
///
/// The panel is capped at 500 entries and lives only in memory, so it is gone the moment
/// the app restarts. That makes intermittent problems (a run that misbehaved overnight, a
/// dialog that opened wrong once) impossible to investigate after the fact. This keeps a
/// durable timeline next to crash.log.
/// </summary>
public static class ActivityLogFile
{
    private const long MaxBytes = 2 * 1024 * 1024;   // rotate at 2 MB

    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppPaths.Root, "activity.log");

    public static void Append(string message, LogLevel level)
    {
        try
        {
            lock (Gate)
            {
                Rotate();
                File.AppendAllText(FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  [{level.ToString().ToUpperInvariant()}]  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    /// <summary>Keep one previous file so the log cannot grow without bound.</summary>
    private static void Rotate()
    {
        var info = new FileInfo(FilePath);
        if (!info.Exists || info.Length < MaxBytes) return;

        var previous = FilePath + ".1";
        if (File.Exists(previous)) File.Delete(previous);
        File.Move(FilePath, previous);
    }
}
