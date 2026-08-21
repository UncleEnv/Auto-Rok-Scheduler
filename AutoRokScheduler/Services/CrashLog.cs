using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace AutoRokScheduler.Services;

/// <summary>
/// Records unexpected errors to disk with their full inner-exception chain.
///
/// Wrappers like <see cref="TargetInvocationException"/> (thrown whenever a delegate
/// is invoked reflectively, e.g. Dispatcher.BeginInvoke) carry the useless message
/// "Exception has been thrown by the target of an invocation" — the real cause is
/// nested inside. Showing only the outer .Message makes such errors undiagnosable,
/// so everything here unwraps first and always keeps the full detail on disk.
/// </summary>
public static class CrashLog
{
    private static readonly object FileLock = new();

    public static string FilePath => Path.Combine(AppPaths.Root, "crash.log");

    /// <summary>Digs past reflection/task wrappers to the exception that actually failed.</summary>
    public static Exception Unwrap(Exception ex)
    {
        for (var guard = 0; guard < 20; guard++)
        {
            switch (ex)
            {
                case TargetInvocationException { InnerException: { } inner }:
                    ex = inner;
                    continue;
                case AggregateException agg when agg.Flatten().InnerExceptions.Count > 0:
                    ex = agg.Flatten().InnerExceptions[0];
                    continue;
                default:
                    return ex;
            }
        }
        return ex;
    }

    /// <summary>Short "Type: message" for the real cause — safe to show a user.</summary>
    public static string Describe(Exception ex)
    {
        var root = Unwrap(ex);
        return $"{root.GetType().Name}: {root.Message}";
    }

    public static void Write(string context, Exception ex)
    {
        try
        {
            var sb = new StringBuilder()
                .AppendLine(new string('=', 72))
                .AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  [{context}]")
                .AppendLine($"Cause: {Describe(ex)}")
                .AppendLine()
                .AppendLine(ex.ToString())   // includes every inner exception + stack traces
                .AppendLine();

            lock (FileLock)
                File.AppendAllText(FilePath, sb.ToString());
        }
        catch
        {
            // Diagnostics must never themselves take the app down.
        }
    }
}
