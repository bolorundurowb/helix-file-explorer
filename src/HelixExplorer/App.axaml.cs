using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Services;
using HelixExplorer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HelixExplorer;

public partial class App : Application
{
    private IHost? _host;

    /// <summary>
    /// Application service provider, exposed so Avalonia-instantiated views (which have parameterless
    /// constructors) can resolve services such as <see cref="IExternalFileDragPayloadBuilder"/>.
    /// Null before the host is built.
    /// </summary>
    public static IServiceProvider? Services { get; private set; }

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
            .ConfigureServices((_, services) => services.AddHelixApplicationServices())
            .Build();

        Services = _host.Services;

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
        _host?.Services.GetService<IArchiveProvider>()?.CleanupExtractedFiles();
        _host?.Dispose();
        _host = null;
        Services = null;
    }
}
