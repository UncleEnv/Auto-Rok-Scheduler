namespace AutoRokScheduler.Models;

/// <summary>The two controls we automate on the App Control page.</summary>
public enum BotAction
{
    Start,
    Stop
}

/// <summary>Which Chromium browser Selenium should drive.</summary>
public enum BrowserKind
{
    Edge,
    Chrome
}

/// <summary>Live state of a profile's automation, shown in the sidebar/status card.</summary>
public enum RunState
{
    Unknown,
    Running,
    Stopped,
    Working,
    Error
}
