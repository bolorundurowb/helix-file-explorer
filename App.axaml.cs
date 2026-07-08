using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using HelixExplorer.ViewModels;

namespace HelixExplorer;

public sealed class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            var window = new Views.MainWindow { DataContext = vm };
            desktop.MainWindow = window;

            // Persist the session when the app exits.
            desktop.ShutdownRequested += (_, _) => vm.SaveSession();

            // Follow the OS light/dark theme without a restart.
            window.Opened += (_, _) => HookSystemTheme(window, vm);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void HookSystemTheme(Avalonia.Controls.TopLevel window, MainWindowViewModel vm)
    {
        var platform = window.PlatformSettings;
        if (platform is null) return;
        try
        {
            vm.NotifySystemThemeChanged(platform.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark);
            platform.ColorValuesChanged += (_, values) =>
                Dispatcher.UIThread.Post(() => vm.NotifySystemThemeChanged(values.ThemeVariant == PlatformThemeVariant.Dark));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HookSystemTheme failed: {ex.Message}");
        }
    }
}
