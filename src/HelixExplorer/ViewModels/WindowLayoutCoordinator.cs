using Avalonia;
using Avalonia.Controls;
using HelixExplorer.Core.Windowing;

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
        var width = Math.Max(MinWindowWidth, settings.WindowWidth.Value);
        var height = Math.Max(MinWindowHeight, settings.WindowHeight.Value);
        var placed = WindowRestoreClamp.Resolve(
            settings.WindowX,
            settings.WindowY,
            width,
            height,
            ReadWorkAreas(window));

        // Saved size is DIPs. Working areas and Position are physical pixels, so only the origin is clamped.
        window.Width = placed.WidthDip;
        window.Height = placed.HeightDip;
        if (placed.X is int x && placed.Y is int y)
            window.Position = new PixelPoint(x, y);

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

    private static ScreenWorkArea[] ReadWorkAreas(Window window)
    {
        var screens = window.Screens;
        if (screens.ScreenCount == 0)
            return [];

        var all = screens.All;
        var areas = new ScreenWorkArea[all.Count];
        for (var i = 0; i < all.Count; i++)
        {
            var screen = all[i];
            var area = screen.WorkingArea;
            areas[i] = new ScreenWorkArea(area.X, area.Y, area.Width, area.Height, screen.Scaling, screen.IsPrimary);
        }

        return areas;
    }
}

public readonly record struct WindowLayoutCapture(
    double Width,
    double Height,
    int X,
    int Y,
    bool IsMaximized,
    double SidebarWidth);
