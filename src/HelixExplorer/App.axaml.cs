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
using HelixExplorer.Views;
using HelixExplorer.Windows;
using HelixExplorer.Windows.Theming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HelixExplorer;

public partial class App : Application
{
    private IHost? _host;
    private WinThemeWatcher? _themeWatcher;

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
            var themeService = _host.Services.GetRequiredService<IThemeService>();
            var settingsStore = _host.Services.GetRequiredService<ISettingsStore>();
            var settings = settingsStore.Load();
            themeService.ApplyTheme(settings.Theme);

            var accentBrushes = _host.Services.GetRequiredService<IAccentBrushService>();
            accentBrushes.ApplyCustomAccent(settings.AccentColorArgb);
            themeService.ThemeChanged += _ => accentBrushes.ApplyCustomAccent(accentBrushes.CustomAccentArgb);

            _themeWatcher = new WinThemeWatcher(themeService, () => settingsStore.Load().Theme);

            if (Resources.TryGetResource("FileSizeConverter", ActualThemeVariant, out var converterObj)
                && converterObj is Converters.FileSizeConverter converter)
            {
                converter.Mode = settings.SizeDisplay;
                mainWindowViewModel.SizeDisplayChanged += mode => converter.Mode = mode;
            }

            if (Resources.TryGetResource("FolderColorConverter", ActualThemeVariant, out var folderColorObj)
                && folderColorObj is Converters.FolderColorConverter folderColorConverter)
            {
                folderColorConverter.FolderColors = _host.Services.GetRequiredService<IFolderColorService>();
            }

            if (Resources.TryGetResource("FolderIconBrushConverter", ActualThemeVariant, out var folderIconBrushObj)
                && folderIconBrushObj is Converters.FolderIconBrushConverter folderIconBrushConverter)
            {
                folderIconBrushConverter.FolderColors = _host.Services.GetRequiredService<IFolderColorService>();
            }

            if (Resources.TryGetResource("EntryIconBrushConverter", ActualThemeVariant, out var iconBrushObj)
                && iconBrushObj is Converters.EntryIconBrushConverter iconBrushConverter)
            {
                iconBrushConverter.FolderColors = _host.Services.GetRequiredService<IFolderColorService>();
            }
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
        _themeWatcher?.Dispose();
        _themeWatcher = null;
        _host?.Dispose();
        _host = null;
    }
}
