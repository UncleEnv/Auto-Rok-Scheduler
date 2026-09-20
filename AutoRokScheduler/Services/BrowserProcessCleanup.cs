using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;

namespace AutoRokScheduler.Services;

/// <summary>
/// Finds and kills browser processes this app orphaned.
///
/// A Chromium user-data-dir may only be open in one browser process at a time. If the app
/// dies while a run is in flight (crash, Task Manager, Windows shutting down mid-run) the
/// msedge/msedgedriver tree it launched keeps running with no one left to quit it — and it
/// holds the profile lock forever. Every later run then fails on launch with
/// "DevToolsActivePort file doesn't exist", so the scheduler silently stops working until
/// someone kills the stray processes by hand.
///
/// <see cref="ProcessJob"/> stops new orphans being created; this rescues a profile still
/// held by an orphan from an older build or a job object that could not be applied.
///
/// Targets are matched on the <c>--user-data-dir</c> in their command line, so only browsers
/// launched by this app against the requested profile are ever touched — the user's own Edge
/// windows use a different directory and are never matched.
/// </summary>
public static class BrowserProcessCleanup
{
    /// <summary>
    /// Kills any browser still holding <paramref name="userDataDir"/>, plus the driver that
    /// spawned it. Returns the number of processes killed.
    /// </summary>
    public static int KillOrphansFor(string userDataDir, Action<string> log)
    {
        var killed = 0;
        try
        {
            var browsers = Find(userDataDir);
            if (browsers.Count == 0) return 0;

            // The driver is the parent of the browser it launched. Its own command line only
            // mentions a port, so the parent link is the one reliable way to identify it —
            // and it guarantees we only kill a driver that owns one of *our* browsers.
            foreach (var parent in browsers.Select(b => b.ParentId).Distinct())
                foreach (var name in DriverNames)
                    if (Kill(parent, name, log)) { killed++; break; }

            // Kill the browser tree after its driver, so the driver cannot react by
            // restarting or re-spawning anything while we are still working through it.
            foreach (var b in browsers)
                foreach (var name in BrowserNames)
                    if (Kill(b.ProcessId, name, log)) { killed++; break; }
        }
        catch (Exception ex)
        {
            // Recovery is best-effort: if WMI is unavailable the launch simply fails as before.
            log($"Could not scan for stray browser processes: {ex.Message}");
        }
        return killed;
    }

    private static readonly string[] BrowserNames = { "msedge", "chrome" };
    private static readonly string[] DriverNames = { "msedgedriver", "chromedriver" };

    private readonly record struct BrowserProcess(int ProcessId, int ParentId);

    /// <summary>
    /// PIDs of every msedge/chrome process running against this exact profile dir.
    /// Also used at launch to enrol a freshly started browser in <see cref="ProcessJob"/>.
    /// </summary>
    public static List<int> BrowsersUsing(string userDataDir)
        => Find(userDataDir).Select(b => b.ProcessId).ToList();

    private static List<BrowserProcess> Find(string userDataDir)
    {
        var result = new List<BrowserProcess>();
        var wanted = Normalise(userDataDir);

        using var searcher = new ManagementObjectSearcher(
            "SELECT ProcessId, ParentProcessId, CommandLine FROM Win32_Process " +
            "WHERE Name = 'msedge.exe' OR Name = 'chrome.exe'");

        foreach (var mo in searcher.Get())
        {
            using (mo)
            {
                if (mo["CommandLine"] is not string cl) continue;

                // Compare the switch's *value*, never a substring of the command line: one
                // profile key can be a prefix of another ("Uncle" / "Uncle Env"), and a
                // substring test would then kill a different account's live browser.
                var dir = UserDataDirOf(cl);
                if (dir == null || !string.Equals(Normalise(dir), wanted, StringComparison.OrdinalIgnoreCase))
                    continue;

                result.Add(new BrowserProcess(
                    Convert.ToInt32(mo["ProcessId"]), Convert.ToInt32(mo["ParentProcessId"])));
            }
        }
        return result;
    }

    /// <summary>
    /// Reads the --user-data-dir value out of a Chromium command line. It appears in three
    /// shapes depending on the process: bare, value-quoted when the path has spaces, and
    /// whole-switch-quoted on the helper processes.
    /// </summary>
    internal static string? UserDataDirOf(string commandLine)
    {
        const string Switch = "--user-data-dir=";
        var at = commandLine.IndexOf(Switch, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;

        var start = at + Switch.Length;
        int end;

        if (at > 0 && commandLine[at - 1] == '"')          // "--user-data-dir=C:\a b"
            end = commandLine.IndexOf('"', start);
        else if (start < commandLine.Length && commandLine[start] == '"')
            end = commandLine.IndexOf('"', ++start);       // --user-data-dir="C:\a b"
        else
            end = commandLine.IndexOf(' ', start);         // --user-data-dir=C:\ab

        if (end < 0) end = commandLine.Length;
        var value = commandLine[start..end];
        return value.Length == 0 ? null : value;
    }

    /// <summary>Canonical form, so trailing slashes and casing never cause a miss.</summary>
    private static string Normalise(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch { return path.Trim(); }
    }

    /// <summary>
    /// Kills a PID only if it is still the process we identified. Re-checking the name closes
    /// the window where the PID was recycled between the scan and the kill.
    /// </summary>
    private static bool Kill(int pid, string expectedName, Action<string> log)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (!p.ProcessName.Equals(expectedName, StringComparison.OrdinalIgnoreCase)) return false;
            p.Kill(entireProcessTree: true);
            p.WaitForExit(5000);
            return true;
        }
        catch (ArgumentException)
        {
            return false; // exited between the scan and now
        }
        catch (Exception ex)
        {
            log($"Could not stop stray {expectedName} (pid {pid}): {ex.Message}");
            return false;
        }
    }
}
