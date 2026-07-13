using HelixExplorer.Core.Theming;
using Microsoft.Win32;

namespace HelixExplorer.Windows.Theming;

/// <summary>
/// Listens for Windows light/dark preference changes and reapplies theme when in System mode.
/// </summary>
public sealed class WinThemeWatcher : IDisposable
{
    private readonly Action<ThemeMode> _applyTheme;
    private readonly Func<ThemeMode> _getConfiguredMode;
    private bool _disposed;

    public WinThemeWatcher(Action<ThemeMode> applyTheme, Func<ThemeMode> getConfiguredMode)
    {
        _applyTheme = applyTheme;
        _getConfiguredMode = getConfiguredMode;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_disposed)
            return;

        if (e.Category != UserPreferenceCategory.General)
            return;

        if (_getConfiguredMode() != ThemeMode.System)
            return;

        _applyTheme(ThemeMode.System);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
