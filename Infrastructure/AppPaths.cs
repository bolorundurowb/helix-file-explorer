using System.IO;

namespace HelixExplorer.Infrastructure;

/// <summary>
/// Resolves the per-user configuration directory (%APPDATA%/HelixExplorer) and
/// builds file paths inside it. Created once; the directory is ensured on first use.
/// </summary>
public static class AppPaths
{
    /// <summary>Root configuration directory, guaranteed to exist.</summary>
    public static string ConfigDir { get; }

    static AppPaths()
    {
        ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HelixExplorer");
        try { Directory.CreateDirectory(ConfigDir); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"AppPaths: {ex.Message}"); }
    }

    /// <summary>Absolute path to <paramref name="fileName"/> within the config directory.</summary>
    public static string File(string fileName) => Path.Combine(ConfigDir, fileName);
}
