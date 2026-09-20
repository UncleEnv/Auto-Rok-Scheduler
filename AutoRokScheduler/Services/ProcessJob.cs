using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AutoRokScheduler.Services;

/// <summary>
/// A Windows job object that kills everything inside it when this app's process ends.
///
/// The browser is a *grandchild* of the app (app → msedgedriver → msedge), so nothing in
/// Windows ties its lifetime to ours: if the app dies without running
/// <see cref="SeleniumBot.Dispose"/> — a crash, End Task, a mid-run sign-out — the browser
/// survives and keeps the profile's user-data-dir locked, which breaks every later run.
/// Disposing SeleniumBot only covers the orderly case; this covers the rest.
///
/// <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c> makes the kernel do the cleanup: when the last
/// handle to the job closes — which happens automatically on process exit, however abrupt —
/// every process still in the job is terminated. So a browser can no longer outlive the app.
/// </summary>
public static class ProcessJob
{
    private static readonly object Gate = new();
    private static IntPtr _job = IntPtr.Zero;
    private static bool _tried;

    /// <summary>
    /// Puts a process (and everything it later spawns) under the app's lifetime.
    /// Best-effort: a failure here costs orphan protection, never the run itself.
    /// </summary>
    public static bool Enroll(int processId, Action<string>? log = null)
    {
        try
        {
            var job = Handle();
            if (job == IntPtr.Zero) return false;

            using var p = Process.GetProcessById(processId);
            if (AssignProcessToJobObject(job, p.Handle)) return true;

            log?.Invoke($"Could not tie the browser to this app's lifetime (error {Marshal.GetLastWin32Error()}).");
            return false;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Could not tie the browser to this app's lifetime: {ex.Message}");
            return false;
        }
    }

    /// <summary>Creates the job on first use; returns <see cref="IntPtr.Zero"/> if unavailable.</summary>
    private static IntPtr Handle()
    {
        lock (Gate)
        {
            if (_tried) return _job;
            _tried = true;

            // Unnamed, so it is private to this process and two running copies of the app
            // never share one job (which would let either one's exit kill the other's browser).
            var job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) return _job = IntPtr.Zero;

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                }
            };

            var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)size))
                {
                    // Without the kill-on-close limit the job is pointless — drop it rather
                    // than hold a handle that gives a false sense of protection.
                    CloseHandle(job);
                    return _job = IntPtr.Zero;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            // The handle is deliberately never closed: the job must stay alive for the whole
            // process lifetime, and letting the kernel close it at exit is exactly the trigger
            // that kills any browser still running.
            return _job = job;
        }
    }

    // ------------------------------------------------------------------ interop

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr securityAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr job, int infoClass, IntPtr info, uint infoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}
