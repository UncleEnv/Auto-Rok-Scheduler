using System;

namespace AutoRokScheduler.Models;

/// <summary>
/// A single scheduled action for a profile: fire <see cref="Action"/> at
/// <see cref="TimeOfDay"/> on the selected <see cref="Days"/>.
/// </summary>
public sealed class ScheduleEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public BotAction Action { get; set; } = BotAction.Start;

    /// <summary>Time of day to fire (local time). Serializes as "HH:mm:ss".</summary>
    public TimeOnly TimeOfDay { get; set; } = new TimeOnly(9, 0);

    /// <summary>
    /// Days this entry runs on. Empty array = every day.
    /// </summary>
    public DayOfWeek[] Days { get; set; } = Array.Empty<DayOfWeek>();

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Last time this entry actually fired. Persisted so a same-day restart
    /// does not re-fire, and so the 30s poll only fires once per day.
    /// </summary>
    public DateTime LastFired { get; set; } = DateTime.MinValue;

    /// <summary>Human-readable day summary, e.g. "Mon-Fri", "Daily", "Sat, Sun".</summary>
    public string DaysSummary()
    {
        if (Days is null || Days.Length == 0) return "Daily";
        if (Days.Length == 7) return "Daily";

        // Detect the common Mon-Fri case.
        var set = new System.Collections.Generic.HashSet<DayOfWeek>(Days);
        var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        if (set.Count == 5 && System.Linq.Enumerable.All(weekdays, set.Contains))
            return "Mon-Fri";

        // Otherwise list short names in week order.
        var order = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        var names = new System.Collections.Generic.List<string>();
        foreach (var d in order)
            if (set.Contains(d)) names.Add(ShortName(d));
        return string.Join(", ", names);
    }

    private static string ShortName(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "Mon",
        DayOfWeek.Tuesday => "Tue",
        DayOfWeek.Wednesday => "Wed",
        DayOfWeek.Thursday => "Thu",
        DayOfWeek.Friday => "Fri",
        DayOfWeek.Saturday => "Sat",
        _ => "Sun"
    };
}
