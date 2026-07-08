using HelixExplorer.Services;

namespace HelixExplorer;

/// <summary>
/// Manual service locator, since Avalonia does not ship with a built-in DI container.
/// Services are constructed lazily on first access — importantly <em>after</em> the
/// Avalonia application is built, so <see cref="ThemeService"/> can read live app state.
/// Note: <c>FileChangeWatcherService</c> is intentionally NOT hosted here; each pane
/// creates its own so panes don't tear down each other's watchers.
/// </summary>
public static class ServiceLocator
{
    private static readonly Lazy<IFileSystemService> s_fileSystem = new(() => new FileSystemService());
    private static readonly Lazy<IContextMenuService> s_contextMenu = new(() => new ContextMenuService());
    private static readonly Lazy<IArchiveService> s_archive = new(() => new ArchiveService());
    private static readonly Lazy<IGitService> s_git = new(() => new GitService());
    private static readonly Lazy<IThemeService> s_theme = new(() => new ThemeService());
    private static readonly Lazy<ISettingsService> s_settings = new(() => new SettingsService());
    private static readonly Lazy<ISessionService> s_session = new(() => new SessionService());

    public static IFileSystemService FileSystem => s_fileSystem.Value;
    public static IContextMenuService ContextMenu => s_contextMenu.Value;
    public static IArchiveService Archive => s_archive.Value;
    public static IGitService Git => s_git.Value;
    public static IThemeService Theme => s_theme.Value;
    public static ISettingsService Settings => s_settings.Value;
    public static ISessionService Session => s_session.Value;
}
