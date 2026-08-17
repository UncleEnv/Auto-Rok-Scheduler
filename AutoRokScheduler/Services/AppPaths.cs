using System;
using System.IO;

namespace AutoRokScheduler.Services;

/// <summary>
/// Central place for on-disk locations. Everything lives under
/// %LocalAppData%\AutoRokScheduler (Local, not Roaming — DPAPI blobs are
/// machine-bound so roaming them would break decryption).
/// </summary>
public static class AppPaths
{
    public static string Root
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AutoRokScheduler");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ConfigFile => Path.Combine(Root, "config.json");

    /// <summary>Parent folder holding all browser user-data-dirs.</summary>
    public static string ProfilesRoot
    {
        get
        {
            var dir = Path.Combine(Root, "BrowserProfiles");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>The user-data-dir for a given browser-profile key.</summary>
    public static string BrowserProfileDir(string key)
    {
        var dir = Path.Combine(ProfilesRoot, key);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
