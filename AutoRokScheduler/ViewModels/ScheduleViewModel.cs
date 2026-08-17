using System;
using AutoRokScheduler.Models;

namespace AutoRokScheduler.ViewModels;

public sealed class ScheduleViewModel : ObservableObject
{
    public ScheduleEntry Model { get; }

    /// <summary>Raised when the user toggles Enabled, so the owner can persist.</summary>
    public event Action? Changed;

    public ScheduleViewModel(ScheduleEntry model) => Model = model;

    public string TimeText => Model.TimeOfDay.ToString("HH:mm");
    public string ActionText => Model.Action.ToString();
    public string DaysText => Model.DaysSummary();

    public bool Enabled
    {
        get => Model.Enabled;
        set
        {
            if (Model.Enabled == value) return;
            Model.Enabled = value;
            OnPropertyChanged();
            Changed?.Invoke();
        }
    }

    /// <summary>Re-read all display fields after an edit.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(DaysText));
        OnPropertyChanged(nameof(Enabled));
    }
}
