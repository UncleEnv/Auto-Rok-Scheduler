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

    /// <summary>Slots that came due and are waiting for the runner to be free.</summary>
    private readonly List<PendingRun> _pending = new();

    /// <summary>When we last became genuinely awake; resets after a sleep/freeze gap.</summary>
    private DateTime _awakeSince = DateTime.Now;
    private DateTime _lastTick;

    /// <summary>True for the whole startup status sweep, which must not be interleaved.</summary>
    private bool _sweeping;

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

        // A save that fails is reported in the log instead of crashing the app.
        Storage.SaveFailed += OnSaveFailed;

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
        _pending.RemoveAll(p => p.Profile == vm);   // don't run slots for a deleted account
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
        // Held for the whole sweep: it releases IsBusy between accounts, so without this a
        // tick landing in the gap could start a scheduled run mid-sweep and make the sweep's
        // own IsBusy guard skip the next account's status check.
        _sweeping = true;
        try
        {
            await RefreshStatusesCoreAsync();
        }
        finally
        {
            _sweeping = false;
            DrainPending();   // run anything that came due during the sweep
        }
    }

    private async Task RefreshStatusesCoreAsync()
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

            // Start any slot that came due while this run was in flight. Going via the
            // dispatcher unwinds this call stack first, so it does not recurse into RunAsync,
            // and the queued action begins in seconds rather than waiting for the next poll.
            _ = _dispatcher.InvokeAsync(DrainPending);   // deliberately not awaited
        }
    }

    // ------------------------------------------------------------ scheduler

    /// <summary>A slot that came due and is waiting for the runner to be free.</summary>
    private readonly record struct PendingRun(
        ProfileViewModel Profile, ScheduleViewModel Schedule, DateTime DueAt);

    /// <summary>
    /// The scheduled time is a hard trigger, so every tick ARMS due entries — even while a
    /// run is in flight — and a separate drain step executes them once the runner frees up.
    /// Previously a tick during a busy period returned immediately and the entry expired
    /// unseen, silently skipping the slot for the day.
    /// </summary>
    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        DetectWakeGap(now);
        ArmDueEntries(now);
        DrainPending();
    }

    /// <summary>
    /// A tick that arrives far later than the poll interval means the machine slept or the
    /// app was frozen — we were not really awake for anything in that gap, so slots inside
    /// it are treated as missed-while-closed rather than fired on resume.
    /// </summary>
    private void DetectWakeGap(DateTime now)
    {
        var poll = TimeSpan.FromSeconds(Math.Max(5, _state.Settings.PollSeconds));
        if (_lastTick != default && now - _lastTick > poll + TimeSpan.FromMinutes(2))
        {
            _awakeSince = now;
            LogInfo("Resumed after a gap (sleep or freeze) — slots missed while away are skipped.", LogLevel.Warn);
        }
        _lastTick = now;
    }

    private void ArmDueEntries(DateTime now)
    {
        var catchUp = _state.Settings.CatchUpMissed;

        foreach (var pvm in Profiles)
        {
            foreach (var svm in pvm.Schedules)
            {
                if (!ScheduleEvaluator.IsDue(svm.Model, now, catchUp, _awakeSince)) continue;
                if (_pending.Any(p => p.Schedule == svm)) continue; // already queued

                _pending.Add(new PendingRun(pvm, svm, now));

                if (IsBusy || _sweeping)
                    LogInfo($"⏰ {pvm.Name} {svm.Model.Action} ({svm.Model.TimeOfDay:HH:mm}) is due — queued, runs as soon as the current action finishes.", LogLevel.Info);
            }
        }
    }

    /// <summary>Runs the next queued slot, if the runner is free.</summary>
    private void DrainPending()
    {
        if (IsBusy || _sweeping || _pending.Count == 0) return;

        var now = DateTime.Now;

        // Anything still queued from a previous day is stale (it would toggle to the wrong
        // state a day late); drop it rather than firing it.
        _pending.RemoveAll(p => p.DueAt.Date != now.Date);
        if (_pending.Count == 0) return;

        // Serve the longest-waiting slot first, so no account is starved.
        var profile = _pending.OrderBy(p => p.DueAt).First().Profile;
        var forProfile = _pending.Where(p => p.Profile == profile)
                                 .OrderBy(p => p.Schedule.Model.TimeOfDay)
                                 .ToList();

        // Several slots for one account can stack up during a long run. The correct end state
        // is the latest one, so run only that and record the rest as superseded — this reaches
        // the same state in one browser run instead of toggling through each in turn.
        var winner = forProfile[^1];
        foreach (var p in forProfile)
        {
            p.Schedule.Model.LastFired = now;  // mark at drain, not arm: a slot still queued
            _pending.Remove(p);                // when the app closes must not look "fired"
            if (p.Schedule != winner.Schedule)
                LogInfo($"⏰ Skipping superseded {profile.Name} {p.Schedule.Model.Action} ({p.Schedule.Model.TimeOfDay:HH:mm}) — catching up to {winner.Schedule.Model.TimeOfDay:HH:mm}.", LogLevel.Info);
        }

        Save();
        LogInfo($"⏰ Schedule fired: {profile.Name} {winner.Schedule.Model.Action} ({winner.Schedule.Model.TimeOfDay:HH:mm}).", LogLevel.Info);
        _ = RunAsync(profile, winner.Schedule.Model.Action);
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

        // InvokeAsync rather than BeginInvoke: the latter dispatches via DynamicInvoke,
        // which masks any failure here as a bare TargetInvocationException.
        _dispatcher.InvokeAsync(() => LogInfo(message, level));
    }

    private void OnSaveFailed(Exception ex)
        => _dispatcher.InvokeAsync(() =>
               LogInfo($"Could not save config — {CrashLog.Describe(ex)}", LogLevel.Error));

    /// <summary>Log a UI-level event (dialogs opening/closing, etc.).</summary>
    public void LogUi(string message) => LogInfo(message, LogLevel.Info);

    private void LogInfo(string message, LogLevel level)
    {
        Log.Add(new LogEntry { Message = message, Level = level });
        while (Log.Count > 500) Log.RemoveAt(0);
        ActivityLogFile.Append(message, level);
    }

    public void Shutdown()
    {
        try { _timer.Stop(); } catch { }
        try { _cts.Cancel(); } catch { }
        Storage.SaveFailed -= OnSaveFailed;
        Save();
    }
}
