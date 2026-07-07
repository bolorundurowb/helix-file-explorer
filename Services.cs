using System.Runtime.CompilerServices;
using HelixExplorer.Services;

namespace HelixExplorer;

/// <summary>
/// Manual service locator, since Avalonia does not ship with a built-in DI
/// container. Resolve services hosted here instead of `new`-ing them ad hoc.
/// </summary>
public static class ServiceLocator
{
    public static IFileSystemService FileSystem { get; } = new FileSystemService();
    public static IContextMenuService ContextMenu { get; } = new ContextMenuService();
    public static IArchiveService Archive { get; } = new ArchiveService();
    public static IGitService Git { get; } = new GitService();
    public static IThemeService Theme { get; } = new ThemeService();
    public static ISettingsService Settings { get; } = new SettingsService();
    public static FileChangeWatcherService Watcher { get; } = new FileChangeWatcherService();

    [ModuleInitializer]
    internal static void Init()
    {
        System.Diagnostics.Debug.WriteLine("Helix Services initialised");
    }
}