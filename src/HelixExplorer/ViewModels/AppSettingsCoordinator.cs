using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Theming;
using HelixExplorer.Services;

namespace HelixExplorer.ViewModels;

/// <summary>
/// Scoped helper for loading/saving <see cref="AppSettings"/> with debounced persistence
/// and applying theme/font/accent when those values change.
/// </summary>
public sealed class AppSettingsCoordinator(
    ISettingsStore settingsStore,
    IThemeService themeService,
    IUiFontService uiFontService,
    IAccentBrushService accentBrushes) : IDisposable
{
    private const int PersistDebounceMs = 300;

    private AppSettings? _cached;
    private CancellationTokenSource? _persistCts;
    private bool _disposed;

    public AppSettings Load() => _cached ??= settingsStore.Load();

    public void ScheduleSave(Action<AppSettings> mutate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var settings = Load();
        var previousTheme = settings.Theme;
        var previousFont = settings.UiFont;
        var previousAccent = settings.AccentColorArgb;

        mutate(settings);
        _cached = settings;

        if (settings.Theme != previousTheme)
            themeService.ApplyTheme(settings.Theme);
        if (settings.UiFont != previousFont)
            uiFontService.ApplyFont(settings.UiFont);
        if (settings.AccentColorArgb != previousAccent)
            accentBrushes.ApplyCustomAccent(settings.AccentColorArgb);

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _persistCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        var token = cts.Token;
        _ = Task.Delay(PersistDebounceMs, token).ContinueWith(
            _ =>
            {
                var pending = _cached;
                if (pending is not null)
                    settingsStore.Save(pending);
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    public void Flush()
    {
        var cts = Interlocked.Exchange(ref _persistCts, null);
        cts?.Cancel();
        cts?.Dispose();

        if (_cached is not null)
            settingsStore.Save(_cached);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Flush();
    }
}
