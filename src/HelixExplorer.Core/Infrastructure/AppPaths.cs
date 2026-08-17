namespace HelixExplorer.Core.Infrastructure;

public static class AppPaths
{
    private const string ProductionFolderName = "HelixExplorer";
    private const string DevelopmentFolderName = "HelixExplorer.Dev";

    /// <summary>
    /// Debug builds always use the development profile. Release builds do too when
    /// <c>HELIX_DEV_PROFILE</c> is <c>1</c>/<c>true</c>, so a local Release run cannot
    /// overwrite the installed app's <c>%AppData%\HelixExplorer</c> data.
    /// </summary>
    public static bool IsDevelopmentProfile { get; } = DetectDevelopmentProfile();

    private static readonly string ProfileFolderName =
        IsDevelopmentProfile ? DevelopmentFolderName : ProductionFolderName;

    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ProfileFolderName);

    private static readonly string LogsRootFolder = Path.Combine(
        Path.GetTempPath(),
        ProfileFolderName,
        "logs");

    private static readonly string TempRootFolder = Path.Combine(
        Path.GetTempPath(),
        ProfileFolderName);

    public static string AppData => AppDataFolder;
    public static string SettingsFile => Path.Combine(AppDataFolder, "settings.json");
    public static string SessionFile => Path.Combine(AppDataFolder, "session.json");
    public static string AppDatabaseFile => Path.Combine(AppDataFolder, "helix.db");

    /// <summary>
    /// Scratch root under the system temp directory (archive extraction, etc.).
    /// </summary>
    public static string TempRoot => TempRootFolder;

    /// <summary>
    /// Root folder for application log files under the system temp directory.
    /// </summary>
    public static string LogsRoot => LogsRootFolder;

    /// <summary>
    /// Version-specific log folder, e.g. <c>%TEMP%\HelixExplorer\logs\0.2.1</c>
    /// (or <c>HelixExplorer.Dev</c> in a development profile).
    /// </summary>
    public static string GetVersionedLogsDirectory(string? version = null)
        => Path.Combine(LogsRootFolder, AppVersion.SanitizeForPath(version ?? AppVersion.Current));

    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(AppDataFolder);
        Directory.CreateDirectory(GetVersionedLogsDirectory());
    }

    private static bool DetectDevelopmentProfile()
    {
        if (IsTruthy(Environment.GetEnvironmentVariable("HELIX_DEV_PROFILE")))
            return true;
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    private static bool IsTruthy(string? value)
        => value is "1" or "true" or "TRUE" or "True" or "yes" or "YES";
}
