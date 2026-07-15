using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Services;

public sealed class AvaloniaUiHost(IWindowOwnerContext ownerContext) : IUiHost
{
    public nint GetMainWindowHandle()
    {
        var window = GetOwnerWindow();
        if (window?.TryGetPlatformHandle() is { } handle)
            return handle.Handle;

        return 0;
    }

    public (int X, int Y) GetPointerScreenPosition()
    {
        // The previous implementation returned the centre of the owner window, which placed the
        // native shell context menu (TrackPopupMenuEx) at an arbitrary spot — usually behind the
        // window — so "Show more options" appeared to do nothing. Use the OS cursor position
        // instead; the user has just right-clicked at the cursor, so that is exactly where the
        // shell menu should appear.
        if (TryGetCursorPos(out var pt))
            return (pt.X, pt.Y);

        try
        {
            if (GetOwnerWindow() is { } window)
            {
                var point = window.PointToScreen(new Point(0, 0));
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
        var clipboard = GetOwnerWindow()?.Clipboard;
        if (clipboard is null)
            return;

        await clipboard.SetTextAsync(text).ConfigureAwait(true);
    }

    private Window? GetOwnerWindow()
        => ownerContext.OwnerWindow ?? GetFallbackMainWindow();

    private static Window? GetFallbackMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;

        return null;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private static bool TryGetCursorPos(out (int X, int Y) point)
    {
        try
        {
            if (GetCursorPos(out var pt))
            {
                point = (pt.X, pt.Y);
                return true;
            }
        }
        catch
        {
        }

        point = default;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
