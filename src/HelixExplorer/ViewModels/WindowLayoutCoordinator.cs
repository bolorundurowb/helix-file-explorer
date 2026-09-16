using Avalonia;
using Avalonia.Controls;

namespace HelixExplorer.ViewModels;

public sealed class WindowLayoutCoordinator(AppSettingsCoordinator settingsCoordinator)
{
    private const double MinWindowWidth = 800;
    private const double MinWindowHeight = 500;

    public bool ShouldRestore { get; private set; }

    public void Initialize(bool restoreSession) => ShouldRestore = restoreSession;

    public void Apply(Window window)
    {
        if (!ShouldRestore)
            return;

        var settings = settingsCoordinator.Load();
        if (settings.WindowWidth is not > 0 || settings.WindowHeight is not > 0)
            return;

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Width = Math.Max(MinWindowWidth, settings.WindowWidth.Value);
        window.Height = Math.Max(MinWindowHeight, settings.WindowHeight.Value);

        if (settings.WindowX.HasValue && settings.WindowY.HasValue)
            window.Position = new PixelPoint(settings.WindowX.Value, settings.WindowY.Value);

        if (settings.WindowMaximized)
            window.WindowState = WindowState.Maximized;
    }

    public void Capture(WindowLayoutCapture capture)
    {
        if (!ShouldRestore)
            return;

        settingsCoordinator.SaveNow(settings =>
        {
            settings.WindowMaximized = capture.IsMaximized;
            if (!capture.IsMaximized)
            {
                settings.WindowWidth = Math.Max(MinWindowWidth, capture.Width);
                settings.WindowHeight = Math.Max(MinWindowHeight, capture.Height);
                settings.WindowX = capture.X;
                settings.WindowY = capture.Y;
            }

            settings.SidebarWidth = ClampSidebarWidth(capture.SidebarWidth);
        });
    }

    public static double ClampSidebarWidth(double width) => Math.Clamp(width, 200, 450);
}

public readonly record struct WindowLayoutCapture(
    double Width,
    double Height,
    int X,
    int Y,
    bool IsMaximized,
    double SidebarWidth);
