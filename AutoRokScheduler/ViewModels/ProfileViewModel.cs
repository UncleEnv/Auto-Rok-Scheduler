using System;
using System.Collections.ObjectModel;
using AutoRokScheduler.Models;

namespace AutoRokScheduler.ViewModels;

public sealed class ProfileViewModel : ObservableObject
{
    public Profile Model { get; }
    public ObservableCollection<ScheduleViewModel> Schedules { get; } = new();

    private RunState _state;
    private DateTime? _stateSince;
    private BotAction? _pendingAction;

    public ProfileViewModel(Profile model)
    {
        Model = model;
        _state = model.LastKnownState;
        foreach (var s in model.Schedules)
            Schedules.Add(new ScheduleViewModel(s));
    }

    public string Name => Model.Name;
    public string Login => Model.Login;
    public string DeviceName => Model.DeviceName;
    public string BrowserText => Model.Browser.ToString();
    public string ProfileKeyText => Model.EffectiveProfileKey;

    /// <summary>Sidebar subtitle.</summary>
    public string Subtitle => string.IsNullOrWhiteSpace(Model.Login) ? "(no login set)" : Model.Login;

    public RunState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                _stateSince = DateTime.Now;
                Model.LastKnownState = value;
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(SinceText));
            }
        }
    }

    /// <summary>Enter the transient "working" state, remembering which action so the
    /// UI can show STARTING… vs STOPPING…</summary>
    public void SetWorking(BotAction action)
    {
        _pendingAction = action;
        State = RunState.Working;
        OnPropertyChanged(nameof(StatusText));
    }

    public string StatusText => _state switch
    {
        RunState.Running => "STARTED",
        RunState.Stopped => "STOPPED",
        RunState.Working => _pendingAction == BotAction.Stop ? "STOPPING…" : "STARTING…",
        RunState.Error => "ERROR",
        _ => "UNKNOWN"
    };

    public string SinceText => _stateSince is { } t ? $"since {t:HH:mm}" : "";

    public void RefreshHeader()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Login));
        OnPropertyChanged(nameof(DeviceName));
        OnPropertyChanged(nameof(BrowserText));
        OnPropertyChanged(nameof(ProfileKeyText));
        OnPropertyChanged(nameof(Subtitle));
    }
}
