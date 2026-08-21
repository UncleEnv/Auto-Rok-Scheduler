using System;
using System.IO;
using System.Threading;
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

    /// <summary>
    /// Raised when a save could not be completed, so the UI can surface it in the
    /// activity log. A failed save must never crash the app: it is called from timer
    /// ticks and UI handlers where an exception would become a modal error dialog.
    /// </summary>
    public static event Action<Exception>? SaveFailed;

    public static void Save(AppState state)
    {
        try
        {
            SaveCore(state);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Storage.Save", ex);
            SaveFailed?.Invoke(ex);
        }
    }

    private static void SaveCore(AppState state)
    {
        var json = JsonSerializer.Serialize(state, Options);
        var target = AppPaths.ConfigFile;
        var tmp = target + ".tmp";

        // Write atomically: temp file + replace, so a crash mid-write can't corrupt config.
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                File.WriteAllText(tmp, json);
                if (File.Exists(target)) File.Replace(tmp, target, null);
                else File.Move(tmp, target);
                return;
            }
            catch (IOException ex)
            {
                // Antivirus, search indexer and backup tools take brief exclusive locks;
                // File.Replace is especially prone to losing that race. Back off and retry.
                last = ex;
                Thread.Sleep(150);
            }
            catch (UnauthorizedAccessException ex)
            {
                last = ex;
                Thread.Sleep(150);
            }
        }

        // Last resort: plain overwrite. Marginally less crash-safe than the swap, but
        // far better than dropping the user's accounts and schedules on the floor.
        try
        {
            File.WriteAllText(target, json);
        }
        catch (Exception ex)
        {
            throw new IOException(
                $"Could not write {target} after 3 attempts: {ex.Message}", last ?? ex);
        }
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
