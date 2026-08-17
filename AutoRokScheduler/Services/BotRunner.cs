using System;
using System.Threading;
using System.Threading.Tasks;
using AutoRokScheduler.Models;

namespace AutoRokScheduler.Services;

/// <summary>
/// Serialises all browser actions (manual + scheduled) so two runs never fight
/// over the same profile dir or WebDriver. Runs the work on a background thread.
/// </summary>
public sealed class BotRunner
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<AppState> _getState;

    /// <summary>Raised (on a background thread) for each log line.</summary>
    public event Action<string>? Log;

    public BotRunner(Func<AppState> getState) => _getState = getState;

    public bool IsBusy => _gate.CurrentCount == 0;

    public async Task<RunState> RunAsync(Profile profile, BotAction action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var state = _getState();
                using var bot = new SeleniumBot(
                    state.Site, state.Settings,
                    msg => Log?.Invoke($"[{profile.Name}] {msg}"));
                return bot.RunAction(profile, action, ct);
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
