using Avalonia.Threading;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Core.Session;
using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Theming;
using HelixExplorer.ViewModels;
using HelixExplorer.ViewModels.Pane;
using HelixExplorer.Views;
#if MACOS
using HelixExplorer.macOS;
using HelixExplorer.macOS.Theming;
#else
using HelixExplorer.Windows;
using HelixExplorer.Windows.Theming;
#endif
using Microsoft.Extensions.DependencyInjection;

namespace HelixExplorer.Services;

/// <summary>
/// Shared composition root for Helix Explorer DI. Used by <see cref="App"/> and tests
/// so registration lifetime regressions are caught against the same wiring.
/// </summary>
public static class HelixServiceRegistration
{
    public static IServiceCollection AddHelixApplicationServices(this IServiceCollection services)
    {
#if MACOS
        services.AddHelixMacServices();
#else
        services.AddHelixWindowsServices();
#endif
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<ISessionStore, JsonSessionStore>();
        services.AddSingleton<IThemeService, AvaloniaThemeService>();
        services.AddSingleton<IUiFontService, AvaloniaUiFontService>();
        services.AddSingleton<IAccentBrushService, AvaloniaAccentBrushService>();
        services.AddSingleton<IClipboardService, InternalClipboardService>();
        services.AddSingleton<IOsFileClipboard, AvaloniaOsFileClipboard>();
        services.AddSingleton<IExternalFileDragPayloadBuilder, AvaloniaExternalFileDragPayloadBuilder>();
        services.AddScoped<IWindowOwnerContext, WindowOwnerContext>();
        services.AddScoped<IUiHost, AvaloniaUiHost>();
        services.AddScoped<IUserDialogService, AvaloniaUserDialogService>();
        services.AddSingleton<IWindowHostService, WindowHostService>();
        services.AddSingleton<IGitProvider, CliGitProvider>();
        services.AddSingleton<IArchiveProvider, SharpCompressArchiveProvider>();
        services.AddSingleton<IFolderColorService, FolderColorService>();
        services.AddSingleton<IFolderViewPreferencesService, FolderViewPreferencesService>();
        services.AddSingleton<FileVisualService>();
        services.AddTransient<IFileChangeWatcher, FileChangeWatcherService>();
        services.AddScoped<Func<IFileChangeWatcher>>(sp => () => sp.GetRequiredService<IFileChangeWatcher>());

#if MACOS
        services.AddSingleton(sp =>
        {
            var themes = sp.GetRequiredService<IThemeService>();
            var store = sp.GetRequiredService<ISettingsStore>();
            return new MacThemeWatcher(
                mode =>
                {
                    if (Dispatcher.UIThread.CheckAccess())
                        themes.ApplyTheme(mode);
                    else
                        Dispatcher.UIThread.Post(() => themes.ApplyTheme(mode));
                },
                () => store.Load().Theme);
        });
#else
        services.AddSingleton(sp =>
        {
            var themes = sp.GetRequiredService<IThemeService>();
            var store = sp.GetRequiredService<ISettingsStore>();
            return new WinThemeWatcher(
                mode =>
                {
                    if (Dispatcher.UIThread.CheckAccess())
                        themes.ApplyTheme(mode);
                    else
                        Dispatcher.UIThread.Post(() => themes.ApplyTheme(mode));
                },
                () => store.Load().Theme);
        });
#endif

        services.AddTransient<PaneRefreshCoordinator>();
        services.AddTransient<PaneFileOperationCoordinator>();
        services.AddTransient<PaneSearchCoordinator>();
        services.AddTransient<PaneShellActionCoordinator>();
        services.AddScoped<IPaneCoordinatorFactory, PaneCoordinatorFactory>();
        services.AddScoped<IPaneViewModelFactory, PaneViewModelFactory>();
        services.AddScoped<AppSettingsCoordinator>();
        services.AddScoped<SidebarViewModel>();
        services.AddScoped<CommandPaletteService>();
        services.AddScoped<TabSessionCoordinator>();
        services.AddSingleton<ApplicationStartupCoordinator>();
        services.AddScoped<HomePageViewModel>();
        services.AddScoped<FileOperationReporter>();
        services.AddScoped<IFileOperationReporter>(sp => sp.GetRequiredService<FileOperationReporter>());
        services.AddScoped<MainWindowViewModel>();
        services.AddTransient<MainWindow>();
        return services;
    }
}