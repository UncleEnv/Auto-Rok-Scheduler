using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AutoRokScheduler.Models;

/// <summary>
/// One stored account: credentials + device name + which browser profile
/// (user-data-dir) it uses, plus its own schedule entries.
/// </summary>
public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Friendly display name shown in the sidebar (e.g. "Uncle").</summary>
    public string Name { get; set; } = "";

    /// <summary>Account email used to sign in.</summary>
    public string Login { get; set; } = "";

    /// <summary>DPAPI-encrypted password (Base64). Never plaintext on disk.</summary>
    public string EncryptedPassword { get; set; } = "";

    /// <summary>The App-Control "device name" (the site calls it that, not "machine name").</summary>
    public string DeviceName { get; set; } = "";

    public BrowserKind Browser { get; set; } = BrowserKind.Edge;

    /// <summary>
    /// Key of the browser profile (user-data-dir folder) this account uses.
    /// Two accounts sharing the same key share cookies/session; a unique key
    /// keeps them fully isolated. Defaults to the account name.
    /// </summary>
    public string BrowserProfileKey { get; set; } = "";

    public List<ScheduleEntry> Schedules { get; set; } = new();

    /// <summary>The last known RUNNING/STOPPED state we observed, for display after restart.</summary>
    public RunState LastKnownState { get; set; } = RunState.Unknown;

    /// <summary>Effective browser-profile key (falls back to a sanitized name).</summary>
    [JsonIgnore]
    public string EffectiveProfileKey =>
        string.IsNullOrWhiteSpace(BrowserProfileKey) ? Sanitize(Name) : Sanitize(BrowserProfileKey);

    public static string Sanitize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "default";
        var chars = s.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (Array.IndexOf(System.IO.Path.GetInvalidFileNameChars(), chars[i]) >= 0)
                chars[i] = '_';
        return new string(chars);
    }
}
