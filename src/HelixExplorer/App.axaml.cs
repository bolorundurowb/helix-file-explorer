using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HelixExplorer.Core.FileSystem;
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
            mainWindow.DataContext = _host.Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = mainWindow;

            var themeService = _host.Services.GetRequiredService<IThemeService>();
            var settings = _host.Services.GetRequiredService<ISettingsStore>().Load();
            themeService.ApplyTheme(settings.Theme);

            if (Resources.TryGetResource("FileSizeConverter", ActualThemeVariant, out var converterObj)
                && converterObj is Converters.FileSizeConverter converter)
            {
                converter.Mode = settings.SizeDisplay;
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
