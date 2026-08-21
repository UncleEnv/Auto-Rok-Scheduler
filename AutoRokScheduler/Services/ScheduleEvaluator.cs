using System;
using System.Linq;
using AutoRokScheduler.Models;

namespace AutoRokScheduler.Services;

/// <summary>Pure logic deciding whether a schedule entry should fire now.</summary>
public static class ScheduleEvaluator
{
    /// <param name="awakeSince">
    /// When the app last became (or resumed being) genuinely awake. A slot that came round
    /// while we were awake must run even if the runner was busy at the time; only a slot
    /// that passed while the app was closed depends on <paramref name="catchUp"/>.
    /// </param>
    public static bool IsDue(ScheduleEntry e, DateTime now, bool catchUp, DateTime awakeSince)
    {
        if (!e.Enabled) return false;

        // Weekday filter (empty = daily).
        if (e.Days is { Length: > 0 } && !e.Days.Contains(now.DayOfWeek))
            return false;

        // Only once per calendar day.
        if (e.LastFired.Date == now.Date)
            return false;

        var scheduled = now.Date + e.TimeOfDay.ToTimeSpan();
        if (now < scheduled)
            return false;

        // The scheduled time is a hard trigger: if the app was awake when it came round the
        // entry is due however late the runner is (it gets queued and drained ASAP). There is
        // deliberately no expiry window here — that is what used to silently drop entries the
        // app was merely too busy to notice. catchUp only rescues slots missed while closed.
        return catchUp || scheduled >= awakeSince;
    }
}
