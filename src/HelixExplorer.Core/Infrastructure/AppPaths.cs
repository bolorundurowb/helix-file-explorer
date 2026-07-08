namespace HelixExplorer.Core.Infrastructure;

public static class AppPaths
{
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HelixExplorer");

    public static string AppData => AppDataFolder;
    public static string SettingsFile => Path.Combine(AppDataFolder, "settings.json");
    public static string SessionFile => Path.Combine(AppDataFolder, "session.json");
    public static string ThemeFile => Path.Combine(AppDataFolder, "theme.json");

    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(AppDataFolder);
    }
}
