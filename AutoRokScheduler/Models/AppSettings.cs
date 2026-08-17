namespace AutoRokScheduler.Models;

/// <summary>Global, non-account settings.</summary>
public sealed class AppSettings
{
    public BrowserKind DefaultBrowser { get; set; } = BrowserKind.Edge;

    /// <summary>Run the browser hidden (no visible window). Off by default so you can watch it.</summary>
    public bool Headless { get; set; } = false;

    // The small "peek" window Selenium opens.
    public int WindowWidth { get; set; } = 920;
    public int WindowHeight { get; set; } = 680;
    public int WindowLeft { get; set; } = 140;
    public int WindowTop { get; set; } = 90;

    /// <summary>How often the scheduler checks for due entries.</summary>
    public int PollSeconds { get; set; } = 30;

    /// <summary>Quit the browser after each action (recommended: session persists on disk anyway).</summary>
    public bool CloseBrowserAfterAction { get; set; } = true;

    /// <summary>
    /// If true, a schedule whose time already passed today (e.g. app started late)
    /// still fires once. If false, entries only fire within a short window of their time.
    /// </summary>
    public bool CatchUpMissed { get; set; } = false;

    /// <summary>Overall timeout for a single automation run.</summary>
    public int ActionTimeoutSeconds { get; set; } = 120;
}
