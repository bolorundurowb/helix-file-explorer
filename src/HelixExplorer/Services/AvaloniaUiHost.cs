using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using HelixExplorer.Core.Infrastructure;
#if MACOS
using AppKit;
#endif

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

    private static bool TryGetCursorPos(out (int X, int Y) point)
    {
#if MACOS
        try
        {
            var pos = NSEvent.CurrentMouseLocation;
            point = ((int)pos.X, (int)pos.Y);
            return true;
        }
        catch
        {
            point = default;
            return false;
        }
#else
        try
        {
            if (GetCursorPos(out var winPt))
            {
                point = (winPt.X, winPt.Y);
                return true;
            }
        }
        catch
        {
        }

        point = default;
        return false;
#endif
    }

#if WINDOWS
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
#endif
}