using System.Collections.Generic;

namespace AutoRokScheduler.Models;

/// <summary>Root object persisted to config.json.</summary>
public sealed class AppState
{
    public int SchemaVersion { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public SiteConfig Site { get; set; } = new();
    public List<Profile> Profiles { get; set; } = new();
}
