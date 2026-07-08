using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Services;

public sealed class AvaloniaUiHost : IUiHost
{
    public nint GetMainWindowHandle()
    {
        var window = GetMainWindow();
        if (window?.TryGetPlatformHandle() is { } handle)
            return handle.Handle;

        return 0;
    }

    public (int X, int Y) GetPointerScreenPosition()
    {
        // Prefer Avalonia window center; shell service falls back to Win32 GetCursorPos when 0,0.
        try
        {
            if (GetMainWindow() is { } window)
            {
                var point = window.PointToScreen(new Point(window.Bounds.Width / 2, window.Bounds.Height / 2));
                return (point.X, point.Y);
            }
        }
        catch
        {
        }

        return (0, 0);
    }

    public async Task SetClipboardTextAsync(string text)
    {
        var clipboard = GetMainWindow()?.Clipboard;
        if (clipboard is null)
            return;

        await clipboard.SetTextAsync(text).ConfigureAwait(true);
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;

        return null;
    }
}
