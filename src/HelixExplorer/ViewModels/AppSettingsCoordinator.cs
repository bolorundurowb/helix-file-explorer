using HelixExplorer.Core.Infrastructure;
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
public sealed class AppSettingsCoordinator : IDisposable
{
    public static readonly TimeSpan PersistDebounceDelay = TimeSpan.FromMilliseconds(300);

    private readonly ISettingsStore _settingsStore;
    private readonly IThemeService _themeService;
    private readonly IUiFontService _uiFontService;
    private readonly IAccentBrushService _accentBrushes;
    private readonly Action<TimeSpan, Action, CancellationToken> _schedule;
    private readonly CancellationTokenScope _persistScope = new();

    private AppSettings? _cached;
    private Action<AppSettings>? _pendingMutation;
    private bool _disposed;

    public AppSettingsCoordinator(
        ISettingsStore settingsStore,
        IThemeService themeService,
        IUiFontService uiFontService,
        IAccentBrushService accentBrushes)
        : this(settingsStore, themeService, uiFontService, accentBrushes, ScheduleOnThreadPool)
    {
    }

    internal AppSettingsCoordinator(
        ISettingsStore settingsStore,
        IThemeService themeService,
        IUiFontService uiFontService,
        IAccentBrushService accentBrushes,
        Action<TimeSpan, Action, CancellationToken> schedule)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(uiFontService);
        ArgumentNullException.ThrowIfNull(accentBrushes);
        ArgumentNullException.ThrowIfNull(schedule);

        _settingsStore = settingsStore;
        _themeService = themeService;
        _uiFontService = uiFontService;
        _accentBrushes = accentBrushes;
        _schedule = schedule;
    }

    public AppSettings Load() => _cached ??= _settingsStore.Load();

    public void ScheduleSave(Action<AppSettings> mutate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(mutate);

        Apply(mutate);
        AddPending(mutate);

        var token = _persistScope.Renew();
        _schedule(PersistDebounceDelay, PersistPending, token);
    }

    public void SaveNow(Action<AppSettings> mutate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(mutate);

        Apply(mutate);
        _persistScope.Cancel();

        var mutation = Compose(Interlocked.Exchange(ref _pendingMutation, null), mutate);
        _settingsStore.Update(mutation);
    }

    public void Flush()
    {
        _persistScope.Cancel();
        PersistPending();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Flush();
        _persistScope.Dispose();
    }

    private static void ScheduleOnThreadPool(TimeSpan delay, Action callback, CancellationToken cancellationToken)
    {
        _ = Task.Delay(delay, cancellationToken).ContinueWith(
            static (_, state) => ((Action)state!).Invoke(),
            callback,
            cancellationToken,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
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
            _themeService.ApplyTheme(settings.Theme);
        if (settings.UiFont != previousFont)
            _uiFontService.ApplyFont(settings.UiFont);
        if (settings.AccentColorArgb != previousAccent)
            _accentBrushes.ApplyCustomAccent(settings.AccentColorArgb);
    }

    private void AddPending(Action<AppSettings> mutate)
    {
        while (true)
        {
            var current = _pendingMutation;
            var next = Compose(current, mutate);
            if (ReferenceEquals(Interlocked.CompareExchange(ref _pendingMutation, next, current), current))
                return;
        }
    }

    private void PersistPending()
    {
        var mutation = Interlocked.Exchange(ref _pendingMutation, null);
        if (mutation is not null)
            _settingsStore.Update(mutation);
    }

    private static Action<AppSettings> Compose(Action<AppSettings>? first, Action<AppSettings> next)
        => first is null ? next : settings => { first(settings); next(settings); };
}
