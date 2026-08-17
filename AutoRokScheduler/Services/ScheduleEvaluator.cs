using System;
using System.Linq;
using AutoRokScheduler.Models;

namespace AutoRokScheduler.Services;

/// <summary>Pure logic deciding whether a schedule entry should fire now.</summary>
public static class ScheduleEvaluator
{
    public static bool IsDue(ScheduleEntry e, DateTime now, bool catchUp)
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

        // catchUp: fire even if we're well past the time (e.g. app started late).
        // otherwise only within a short window so we don't surprise-toggle hours later.
        return catchUp || now < scheduled.AddMinutes(3);
    }
}
