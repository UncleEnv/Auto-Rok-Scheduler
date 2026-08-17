using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoRokScheduler.Models;

namespace AutoRokScheduler.Services;

/// <summary>Loads/saves the whole <see cref="AppState"/> to config.json.</summary>
public static class Storage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppState Load()
    {
        try
        {
            if (!File.Exists(AppPaths.ConfigFile))
                return new AppState();
            var json = File.ReadAllText(AppPaths.ConfigFile);
            var state = JsonSerializer.Deserialize<AppState>(json, Options);
            return state ?? new AppState();
        }
        catch
        {
            // Corrupt config: keep a backup and start fresh rather than crashing.
            TryBackupCorrupt();
            return new AppState();
        }
    }

    public static void Save(AppState state)
    {
        var json = JsonSerializer.Serialize(state, Options);
        // Write atomically: temp file + replace, so a crash mid-write can't corrupt config.
        var tmp = AppPaths.ConfigFile + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(AppPaths.ConfigFile))
            File.Replace(tmp, AppPaths.ConfigFile, null);
        else
            File.Move(tmp, AppPaths.ConfigFile);
    }

    private static void TryBackupCorrupt()
    {
        try
        {
            if (File.Exists(AppPaths.ConfigFile))
                File.Copy(AppPaths.ConfigFile, AppPaths.ConfigFile + ".corrupt", overwrite: true);
        }
        catch { /* best effort */ }
    }
}
