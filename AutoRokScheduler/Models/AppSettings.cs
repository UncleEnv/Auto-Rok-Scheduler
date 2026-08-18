namespace AutoRokScheduler.Models;

/// <summary>Global, non-account settings.</summary>
public sealed class AppSettings
{
    public BrowserKind DefaultBrowser { get; set; } = BrowserKind.Edge;

    /// <summary>Run the browser fully hidden — no visible browser or driver-console
    /// window. On by default so automation runs silently in the background.</summary>
    public bool Headless { get; set; } = true;

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
    /// If true, a schedule whose time already passed today (e.g. the app was closed
    /// during its slot) still fires once when the app is next running — catching up to
    /// the correct state. If false, entries only fire within a short window of their
    /// time and are otherwise skipped for the day. On by default so a missed slot
    /// (e.g. a scheduled Stop while the app was closed) isn't silently lost.
    /// </summary>
    public bool CatchUpMissed { get; set; } = true;

    /// <summary>Overall timeout for a single automation run.</summary>
    public int ActionTimeoutSeconds { get; set; } = 120;
}
