namespace HelixExplorer.Core.Infrastructure;

public static class AppPaths
{
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HelixExplorer");

    private static readonly string LogsRootFolder = Path.Combine(
        Path.GetTempPath(),
        "HelixExplorer",
        "logs");

    public static string AppData => AppDataFolder;
    public static string SettingsFile => Path.Combine(AppDataFolder, "settings.json");
    public static string SessionFile => Path.Combine(AppDataFolder, "session.json");

    /// <summary>
    /// Root folder for application log files under the system temp directory.
    /// </summary>
    public static string LogsRoot => LogsRootFolder;

    /// <summary>
    /// Version-specific log folder, e.g. <c>%TEMP%\HelixExplorer\logs\0.2.1</c>.
    /// </summary>
    public static string GetVersionedLogsDirectory(string? version = null)
        => Path.Combine(LogsRootFolder, AppVersion.SanitizeForPath(version ?? AppVersion.Current));

    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(AppDataFolder);
        Directory.CreateDirectory(GetVersionedLogsDirectory());
    }
}
