using Foundation;
using HelixExplorer.Core.Theming;

namespace HelixExplorer.macOS.Theming;

public sealed class MacThemeWatcher : IDisposable
{
    private readonly Action<ThemeMode> _applyTheme;
    private readonly Func<ThemeMode> _getConfiguredMode;
    private readonly NSObject? _themeObserver;
    private int _disposed;

    public MacThemeWatcher(Action<ThemeMode> applyTheme, Func<ThemeMode> getConfiguredMode)
    {
        _applyTheme = applyTheme;
        _getConfiguredMode = getConfiguredMode;

        try
        {
            var center = NSDistributedNotificationCenter.DefaultCenter;
            _themeObserver = center.AddObserver(
                new NSString("AppleInterfaceThemeChangedNotification"),
                notification => OnThemeChanged(notification));
        }
        catch
        {
        }
    }

    private void OnThemeChanged(NSNotification notification)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        if (_getConfiguredMode() != ThemeMode.System)
            return;
        _applyTheme(ThemeMode.System);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (_themeObserver is not null)
            NSDistributedNotificationCenter.DefaultCenter.RemoveObserver(_themeObserver);
    }
}