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

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            var mainWindowViewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
            mainWindow.DataContext = mainWindowViewModel;
            desktop.MainWindow = mainWindow;

            var themeService = _host.Services.GetRequiredService<IThemeService>();
            var settings = _host.Services.GetRequiredService<ISettingsStore>().Load();
            themeService.ApplyTheme(settings.Theme);

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
        services.AddSingleton<IClipboardService, InternalClipboardService>();
        services.AddSingleton<IOsFileClipboard, AvaloniaOsFileClipboard>();
        services.AddSingleton<IUiHost, AvaloniaUiHost>();
        services.AddSingleton<IGitProvider, CliGitProvider>();
        services.AddSingleton<IArchiveProvider, SharpCompressArchiveProvider>();
        services.AddSingleton<IFolderColorService, FolderColorService>();
        services.AddSingleton<FileVisualService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<MainWindow>();
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_host?.Services.GetService(typeof(MainWindowViewModel)) is MainWindowViewModel vm)
            vm.Dispose();

        _host?.Dispose();
        _host = null;
    }
}
