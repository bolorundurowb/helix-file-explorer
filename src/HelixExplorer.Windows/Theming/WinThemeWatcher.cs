using HelixExplorer.Core.Theming;
using Microsoft.Win32;

namespace HelixExplorer.Windows.Theming;

/// <summary>
/// Listens for Windows light/dark preference changes and reapplies theme when in System mode.
/// </summary>
/// <remarks>
/// <see cref="SystemEvents.UserPreferenceChanged"/> is raised on a dedicated system thread, not the
/// UI thread. The supplied <paramref name="applyTheme"/> delegate is therefore responsible for
/// marshalling any UI mutation onto the UI thread (the composition root does this via the Avalonia
/// dispatcher). Disposal is made race-safe against a concurrently firing event via an interlocked
/// flag so unsubscription and event delivery cannot corrupt each other.
/// </remarks>
public sealed class WinThemeWatcher : IDisposable
{
    private readonly Action<ThemeMode> _applyTheme;
    private readonly Func<ThemeMode> _getConfiguredMode;

    // 0 = live, 1 = disposed. Accessed from both the system-events thread and the disposing thread.
    private int _disposed;

    public WinThemeWatcher(Action<ThemeMode> applyTheme, Func<ThemeMode> getConfiguredMode)
    {
        _applyTheme = applyTheme;
        _getConfiguredMode = getConfiguredMode;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        if (e.Category != UserPreferenceCategory.General)
            return;

        if (_getConfiguredMode() != ThemeMode.System)
            return;

        // Marshalling to the UI thread is the delegate's responsibility (see remarks).
        _applyTheme(ThemeMode.System);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
