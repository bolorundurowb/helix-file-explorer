using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Theming;

namespace HelixExplorer.ViewModels;

/// <summary>
/// Scoped helper for loading/saving <see cref="AppSettings"/> with debounced persistence and
/// applying theme/font/accent when those values change.
/// Persistence is field-level and last-write-wins: each save re-reads the on-disk settings and
/// applies only the mutated fields, so concurrent windows interleave their edits instead of one
/// window overwriting another's changes with a stale snapshot.
/// </summary>
public sealed class AppSettingsCoordinator(
    ISettingsStore settingsStore,
    IThemeService themeService,
    IUiFontService uiFontService,
    IAccentBrushService accentBrushes) : IDisposable
{
    private const int PersistDebounceMs = 300;

    private AppSettings? _cached;
    private Action<AppSettings>? _pendingMutation;
    private CancellationTokenSource? _persistCts;
    private bool _disposed;

    public AppSettings Load() => _cached ??= settingsStore.Load();

    public void ScheduleSave(Action<AppSettings> mutate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(mutate);

        Apply(mutate);
        _pendingMutation = Compose(_pendingMutation, mutate);

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _persistCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        var token = cts.Token;
        _ = Task.Delay(PersistDebounceMs, token).ContinueWith(
            _ => PersistPending(),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    public void SaveNow(Action<AppSettings> mutate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(mutate);

        Apply(mutate);

        var cts = Interlocked.Exchange(ref _persistCts, null);
        cts?.Cancel();
        cts?.Dispose();

        var mutation = Compose(Interlocked.Exchange(ref _pendingMutation, null), mutate);
        settingsStore.Update(mutation);
    }

    public void Flush()
    {
        var cts = Interlocked.Exchange(ref _persistCts, null);
        cts?.Cancel();
        cts?.Dispose();

        PersistPending();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Flush();
    }

    private void Apply(Action<AppSettings> mutate)
    {
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
    }

    private void PersistPending()
    {
        var mutation = Interlocked.Exchange(ref _pendingMutation, null);
        if (mutation is not null)
            settingsStore.Update(mutation);
    }

    private static Action<AppSettings> Compose(Action<AppSettings>? first, Action<AppSettings> next)
        => first is null ? next : settings => { first(settings); next(settings); };
}
