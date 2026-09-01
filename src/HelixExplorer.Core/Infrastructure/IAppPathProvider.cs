namespace HelixExplorer.Core.Infrastructure;

public interface IAppPathProvider
{
    string AppData { get; }
    string SettingsFile { get; }
    string SessionFile { get; }
    string AppDatabaseFile { get; }
    string TempRoot { get; }
    string LogsRoot { get; }
    string GetVersionedLogsDirectory(string? version = null);
}

public sealed class DefaultAppPathProvider : IAppPathProvider
{
    public static DefaultAppPathProvider Instance { get; } = new();

    private DefaultAppPathProvider()
    {
    }

    public string AppData => AppPaths.AppData;
    public string SettingsFile => AppPaths.SettingsFile;
    public string SessionFile => AppPaths.SessionFile;
    public string AppDatabaseFile => AppPaths.AppDatabaseFile;
    public string TempRoot => AppPaths.TempRoot;
    public string LogsRoot => AppPaths.LogsRoot;
    public string GetVersionedLogsDirectory(string? version = null) => AppPaths.GetVersionedLogsDirectory(version);
}
