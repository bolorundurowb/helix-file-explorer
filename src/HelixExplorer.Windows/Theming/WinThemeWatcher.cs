using HelixExplorer.Core.Theming;
using Microsoft.Win32;

namespace HelixExplorer.Windows.Theming;

/// <summary>
/// Reapplies theme on Windows light/dark preference changes when configured for System mode, and
/// on contrast theme changes in any mode.
/// </summary>
/// <remarks>
/// <see cref="SystemEvents.UserPreferenceChanged"/> is raised on a dedicated system thread, not the
/// UI thread. The supplied <paramref name="applyTheme"/> delegate is therefore responsible for
/// marshalling any UI mutation onto the UI thread (the composition root does this via the Avalonia
/// dispatcher). Disposal is made race-safe against a concurrently firing event via an interlocked
/// flag so unsubscription and event delivery cannot corrupt each other.
/// The callback is invoked with <see cref="ThemeMode.System"/> even though that mode is unchanged.
/// <c>RequestedThemeVariant</c> stays <c>Default</c> in that case, so the theme service must bump
/// its own revision for name brushes to resolve again. The new light/dark bit is read from
/// <see cref="TryReadAppsUseLightTheme"/> on the UI thread inside that apply, after the registry write.
/// A contrast theme overrides the configured mode, so turning one on or off (or switching between
/// contrast themes) re-applies whatever mode is configured and the theme service picks the palette.
/// </remarks>
public sealed class WinThemeWatcher : IDisposable
{
    private readonly Action<ThemeMode> _applyTheme;
    private readonly Func<ThemeMode> _getConfiguredMode;
    private readonly IHighContrastSource _highContrast;

    // 0 = live, 1 = disposed. Accessed from both the system-events thread and the disposing thread.
    private int _disposed;

    // 1 while a contrast theme was active at the last check, so turning it off also re-applies.
    private int _highContrastActive;

    public WinThemeWatcher(
        Action<ThemeMode> applyTheme,
        Func<ThemeMode> getConfiguredMode,
        IHighContrastSource highContrast)
    {
        _applyTheme = applyTheme;
        _getConfiguredMode = getConfiguredMode;
        _highContrast = highContrast;
        _highContrastActive = highContrast.TryReadActivePalette() is null ? 0 : 1;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        switch (e.Category)
        {
            case UserPreferenceCategory.General:
                if (_getConfiguredMode() == ThemeMode.System)
                    _applyTheme(ThemeMode.System);
                break;

            case UserPreferenceCategory.Accessibility:
            case UserPreferenceCategory.Color:
                var active = _highContrast.TryReadActivePalette() is not null;
                var wasActive = Interlocked.Exchange(ref _highContrastActive, active ? 1 : 0) != 0;
                if (active || wasActive)
                    _applyTheme(_getConfiguredMode());
                break;
        }
    }

    /// <summary>
    /// Windows app theme: 1 light, 0 dark. Null when the value cannot be read.
    /// </summary>
    public static int? TryReadAppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") as int?;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
