using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Services;

public sealed class AvaloniaUiHost : IUiHost
{
    private readonly IWindowOwnerContext _ownerContext;

    public AvaloniaUiHost(IWindowOwnerContext ownerContext)
    {
        _ownerContext = ownerContext;
    }

    public nint GetMainWindowHandle()
    {
        var window = GetOwnerWindow();
        if (window?.TryGetPlatformHandle() is { } handle)
            return handle.Handle;

        return 0;
    }

    public (int X, int Y) GetPointerScreenPosition()
    {
        try
        {
            if (GetOwnerWindow() is { } window)
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
        var clipboard = GetOwnerWindow()?.Clipboard;
        if (clipboard is null)
            return;

        await clipboard.SetTextAsync(text).ConfigureAwait(true);
    }

    private Window? GetOwnerWindow()
        => _ownerContext.OwnerWindow ?? GetFallbackMainWindow();

    private static Window? GetFallbackMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;

        return null;
    }
}
