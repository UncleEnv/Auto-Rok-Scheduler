using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AutoRokScheduler.Models;
using AutoRokScheduler.Services;

namespace AutoRokScheduler.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly BotRunner _runner;
    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher;
    private CancellationTokenSource _cts = new();

    public ObservableCollection<ProfileViewModel> Profiles { get; } = new();
    public ObservableCollection<LogEntry> Log { get; } = new();

    public AppState State => _state;
    public AppSettings Settings => _state.Settings;
    public SiteConfig Site => _state.Site;

    public MainViewModel()
    {
        _dispatcher = Application.Current.Dispatcher;
        _state = Storage.Load();
        _runner = new BotRunner(() => _state);
        _runner.Log += OnBotLog;

        foreach (var p in _state.Profiles)
            Profiles.Add(BuildProfileVm(p));

        Selected = Profiles.FirstOrDefault();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(5, _state.Settings.PollSeconds)) };
        _timer.Tick += OnTick;
        _timer.Start();

        LogInfo("Ready. " + (Profiles.Count == 0 ? "Add an account to begin." : $"{Profiles.Count} account(s) loaded."), LogLevel.Info);
    }

    private ProfileViewModel? _selected;
    public ProfileViewModel? Selected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) OnPropertyChanged(nameof(HasSelection)); }
    }
    public bool HasSelection => _selected != null;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(NotBusy)); }
    }
    public bool NotBusy => !_isBusy;

    // ---------------------------------------------------------- profile CRUD

    private ProfileViewModel BuildProfileVm(Profile p)
    {
        var vm = new ProfileViewModel(p);
        foreach (var s in vm.Schedules) s.Changed += Save;
        return vm;
    }

    public ProfileViewModel AddProfile(Profile p)
    {
        _state.Profiles.Add(p);
        var vm = BuildProfileVm(p);
        Profiles.Add(vm);
        Selected = vm;
        Save();
        LogInfo($"Added account '{p.Name}'.", LogLevel.Success);
        return vm;
    }

    public void CommitProfileEdit(ProfileViewModel vm)
    {
        vm.RefreshHeader();
        Save();
        LogInfo($"Saved account '{vm.Name}'.", LogLevel.Info);
    }

    public void RemoveProfile(ProfileViewModel vm)
    {
        _state.Profiles.Remove(vm.Model);
        Profiles.Remove(vm);
        if (Selected == vm) Selected = Profiles.FirstOrDefault();
        Save();
        LogInfo($"Removed account '{vm.Name}'.", LogLevel.Warn);
    }

    public string[] ExistingProfileKeys()
        => _state.Profiles.Select(p => p.EffectiveProfileKey)
                          .Distinct(StringComparer.OrdinalIgnoreCase)
                          .OrderBy(x => x).ToArray();

    // --------------------------------------------------------- schedule CRUD

    public void AddSchedule(ProfileViewModel pvm, ScheduleEntry entry)
    {
        pvm.Model.Schedules.Add(entry);
        var svm = new ScheduleViewModel(entry);
        svm.Changed += Save;
        pvm.Schedules.Add(svm);
        Save();
        LogInfo($"{pvm.Name}: added {entry.Action} at {entry.TimeOfDay:HH:mm} ({entry.DaysSummary()}).", LogLevel.Info);
    }

    public void CommitScheduleEdit(ScheduleViewModel svm)
    {
        // A just-edited entry is a fresh intention, so clear its "fired today" marker.
        // Otherwise the once-per-day guard blocks an entry that already fired earlier
        // today (e.g. re-pointing a slot that ran this morning to a new Start/Stop that
        // should run now). With catch-up on, an overdue edit then fires on the next tick.
        svm.Model.LastFired = default;
        svm.Refresh();
        Save();
        LogInfo($"{svm.Model.Action} at {svm.Model.TimeOfDay:HH:mm} updated ({svm.Model.DaysSummary()}).", LogLevel.Info);
    }

    public void RemoveSchedule(ProfileViewModel pvm, ScheduleViewModel svm)
    {
        pvm.Model.Schedules.Remove(svm.Model);
        pvm.Schedules.Remove(svm);
        Save();
    }

    // -------------------------------------------------------------- actions

    public async void StartSelected() => await RunAsync(Selected, BotAction.Start);
    public async void StopSelected() => await RunAsync(Selected, BotAction.Stop);

    /// <summary>
    /// On app startup: for each account, log in, open the machine and read the live
    /// status so the dashboard reflects reality instead of the last-saved guess.
    /// </summary>
    public async Task RefreshStatusesAsync()
    {
        foreach (var pvm in Profiles)
        {
            if (IsBusy) continue; // a manual/scheduled action is running; it will set the state
            if (_cts.IsCancellationRequested) { _cts.Dispose(); _cts = new CancellationTokenSource(); }

            IsBusy = true;
            pvm.SetChecking();
            LogInfo($"{pvm.Name}: checking current status…", LogLevel.Info);
            try
            {
                var result = await _runner.RefreshStatusAsync(pvm.Model, _cts.Token);
                pvm.State = result;
                LogInfo($"{pvm.Name}: status is {result}.", LogLevel.Success);
                Save();
            }
            catch (OperationCanceledException)
            {
                pvm.State = RunState.Unknown;
                LogInfo($"{pvm.Name}: status check cancelled.", LogLevel.Warn);
            }
            catch (Exception ex)
            {
                pvm.State = RunState.Error;
                LogInfo($"{pvm.Name}: status check failed — {ex.Message}", LogLevel.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    public void CancelCurrent()
    {
        if (IsBusy)
        {
            LogInfo("Cancelling current action…", LogLevel.Warn);
            _cts.Cancel();
        }
    }

    private async Task RunAsync(ProfileViewModel? pvm, BotAction action)
    {
        if (pvm == null) { LogInfo("No account selected.", LogLevel.Warn); return; }
        if (IsBusy) { LogInfo("Busy — another action is already running.", LogLevel.Warn); return; }

        if (_cts.IsCancellationRequested) { _cts.Dispose(); _cts = new CancellationTokenSource(); }

        IsBusy = true;
        pvm.SetWorking(action);
        LogInfo($"{pvm.Name}: {action} requested…", LogLevel.Info);
        try
        {
            var result = await _runner.RunAsync(pvm.Model, action, _cts.Token);
            pvm.State = result;
            LogInfo($"{pvm.Name}: {action} → {result}.", LogLevel.Success);
            Save();
        }
        catch (OperationCanceledException)
        {
            pvm.State = RunState.Unknown;
            LogInfo($"{pvm.Name}: {action} cancelled.", LogLevel.Warn);
        }
        catch (Exception ex)
        {
            pvm.State = RunState.Error;
            LogInfo($"{pvm.Name}: {action} FAILED — {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ------------------------------------------------------------ scheduler

    private void OnTick(object? sender, EventArgs e)
    {
        if (IsBusy) return; // don't overlap; day/window guard keeps it correct next tick
        var now = DateTime.Now;
        var catchUp = _state.Settings.CatchUpMissed;

        foreach (var pvm in Profiles)
        {
            // Every entry that's due for this profile right now, earliest-scheduled first.
            var due = pvm.Schedules
                .Where(svm => ScheduleEvaluator.IsDue(svm.Model, now, catchUp))
                .OrderBy(svm => svm.Model.TimeOfDay)
                .ToList();
            if (due.Count == 0) continue;

            // The correct end-state is the LATEST-scheduled due entry (e.g. if the app
            // was closed all morning and both a Stop and a later Start are overdue, we
            // want to end Started, not toggle Stop→Start). Mark the superseded ones fired
            // so they don't retroactively toggle, and run only the winner.
            var winner = due[^1];
            foreach (var svm in due)
            {
                svm.Model.LastFired = now; // mark before running → no double-fire
                if (svm != winner)
                    LogInfo($"⏰ Skipping overdue {pvm.Name} {svm.Model.Action} ({svm.Model.TimeOfDay:HH:mm}) — catching up to {winner.Model.TimeOfDay:HH:mm}.", LogLevel.Info);
            }
            Save();
            LogInfo($"⏰ Schedule fired: {pvm.Name} {winner.Model.Action} ({winner.Model.TimeOfDay:HH:mm}).", LogLevel.Info);
            _ = RunAsync(pvm, winner.Model.Action);
            return; // one profile at a time; others catch the next tick
        }
    }

    public void LogSettingsSaved() => LogInfo("Settings saved.", LogLevel.Info);

    public void RestartTimer()
    {
        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(5, _state.Settings.PollSeconds));
        _timer.Start();
    }

    // ------------------------------------------------------------- plumbing

    public void Save() => Storage.Save(_state);

    private void OnBotLog(string message)
    {
        var level = LogLevel.Info;
        var lower = message.ToLowerInvariant();
        if (lower.Contains("fail") || lower.Contains("error") || lower.Contains("reject") || lower.Contains("timed out"))
            level = LogLevel.Error;
        else if (lower.Contains("confirmed") || lower.Contains("ready") || lower.Contains("nothing to do"))
            level = LogLevel.Success;
        else if (lower.Contains("maintenance") || lower.Contains("cancel"))
            level = LogLevel.Warn;

        _dispatcher.BeginInvoke(() => LogInfo(message, level));
    }

    private void LogInfo(string message, LogLevel level)
    {
        Log.Add(new LogEntry { Message = message, Level = level });
        while (Log.Count > 500) Log.RemoveAt(0);
    }

    public void Shutdown()
    {
        try { _timer.Stop(); } catch { }
        try { _cts.Cancel(); } catch { }
        Save();
    }
}
