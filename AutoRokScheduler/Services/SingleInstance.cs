using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace AutoRokScheduler.Services;

/// <summary>
/// Keeps one copy of the app running per Windows user.
///
/// Two copies do not just duplicate work, they actively corrupt each other. Each ticks its
/// own scheduler against the same accounts, and a Chromium user-data-dir may only be open in
/// one process at a time — so whichever fires second cannot launch its browser, and its
/// orphan recovery then kills the *other* instance's live browser mid-run. Both also own
/// config.json, so the last one to save silently discards the other's schedule edits and
/// LastFired stamps, which can make a slot fire twice or not at all.
///
/// This is easy to hit by accident: an old build left in a `bin\` folder, or a pinned
/// shortcut to a previous download, looks identical to the current one once running.
/// </summary>
public static class SingleInstance
{
    // Local\ (per logon session) is deliberate: config.json and the DPAPI-encrypted
    // passwords are per-user, so two different Windows users may each run their own copy.
    private const string MutexName = @"Local\AutoRokScheduler.SingleInstance";

    /// <summary>Held for the life of the process; static so it is never collected.</summary>
    private static Mutex? _held;

    /// <summary>
    /// True when this process may run. False when another instance already owns the app —
    /// in which case that window is brought to the front, so clicking the shortcut does
    /// something visible instead of appearing to do nothing.
    /// </summary>
    public static bool TryAcquire()
    {
        try
        {
            _held = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (createdNew) return true;

            // Not the creator — but the owner may have died without releasing.
            if (_held.WaitOne(TimeSpan.Zero)) return true;
        }
        catch (AbandonedMutexException)
        {
            return true;   // previous owner was killed; ownership passes to us
        }
        catch (Exception)
        {
            // A machine policy or an odd session can make named objects unavailable. Never
            // let this guard be the reason the scheduler will not start.
            return true;
        }

        TryFocusExistingWindow();
        return false;
    }

    /// <summary>Restores and focuses the already-running instance's window, if it has one.</summary>
    private static void TryFocusExistingWindow()
    {
        try
        {
            var me = Environment.ProcessId;
            foreach (var p in Process.GetProcessesByName("AutoRokScheduler"))
            {
                using (p)
                {
                    if (p.Id == me || p.MainWindowHandle == IntPtr.Zero) continue;
                    ShowWindow(p.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(p.MainWindowHandle);
                    return;
                }
            }
        }
        catch
        {
            // Focusing is a courtesy; failing to do it must not block exit.
        }
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
