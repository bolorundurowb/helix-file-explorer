using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Core.Session;
using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Theming;
using HelixExplorer.Services;
using HelixExplorer.ViewModels;
using HelixExplorer.ViewModels.Pane;
using HelixExplorer.Views;
using HelixExplorer.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HelixExplorer;

public partial class App : Application
{
    private IHost? _host;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppPaths.EnsureDirectoriesExist();

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
            })
            .ConfigureServices(ConfigureServices)
            .Build();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownRequested += OnShutdownRequested;

            var windowHost = _host.Services.GetRequiredService<IWindowHostService>();
            var initialPath = ParseInitialPath(Program.StartupArgs);
            var mainWindow = windowHost.OpenWindowAsync(
                initialPath: initialPath,
                restoreSession: initialPath is null).GetAwaiter().GetResult();
            desktop.MainWindow = mainWindow;

            var mainWindowViewModel = (MainWindowViewModel)mainWindow.DataContext!;
            var startupCoordinator = _host.Services.GetRequiredService<ApplicationStartupCoordinator>();
            startupCoordinator.Initialize(this, mainWindowViewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        services.AddHelixWindowsServices();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<ISessionStore, JsonSessionStore>();
        services.AddSingleton<IThemeService, AvaloniaThemeService>();
        services.AddSingleton<IAccentBrushService, AvaloniaAccentBrushService>();
        services.AddSingleton<IClipboardService, InternalClipboardService>();
        services.AddSingleton<IOsFileClipboard, AvaloniaOsFileClipboard>();
        services.AddScoped<IWindowOwnerContext, WindowOwnerContext>();
        services.AddScoped<IUiHost, AvaloniaUiHost>();
        services.AddScoped<IUserDialogService, AvaloniaUserDialogService>();
        services.AddSingleton<IWindowHostService, WindowHostService>();
        services.AddSingleton<IGitProvider, CliGitProvider>();
        services.AddSingleton<IArchiveProvider, SharpCompressArchiveProvider>();
        services.AddSingleton<IFolderColorService, FolderColorService>();
        services.AddSingleton<FileVisualService>();
        services.AddTransient<PaneRefreshCoordinator>();
        services.AddTransient<PaneFileOperationCoordinator>();
        services.AddSingleton<IPaneCoordinatorFactory, PaneCoordinatorFactory>();
        services.AddSingleton<ApplicationStartupCoordinator>();
        services.AddSingleton<HomePageViewModel>();
        services.AddTransient<FileOperationReporter>();
        services.AddTransient<IFileOperationReporter>(sp => sp.GetRequiredService<FileOperationReporter>());
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MainWindow>();
    }

    private static string? ParseInitialPath(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--path" or "-p" && i + 1 < args.Length)
                return args[++i];
        }

        return null;
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        _host?.Services.GetService<ApplicationStartupCoordinator>()?.Dispose();
        _host?.Dispose();
        _host = null;
    }
}
