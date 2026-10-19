using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Theming;

namespace HelixExplorer.ViewModels.Tests;

public sealed class AppSettingsCoordinatorTests
{
    [Fact]
    public void SaveNow_PersistsViaMergeUpdate_NotWholeDocumentSave()
    {
        var store = new RecordingSettingsStore(new AppSettings { Theme = ThemeMode.Light });
        var coordinator = CreateCoordinator(store, out _, out _);

        coordinator.SaveNow(settings => settings.SidebarWidth = 320);

        store.UpdateCalls.Must().Be(1);
        store.SaveCalls.Must().Be(0);
        store.Current.SidebarWidth.Must().Be(320);
        store.Current.Theme.Must().Be(ThemeMode.Light);
    }

    [Fact]
    public void Flush_WithNoPendingMutation_DoesNotWrite()
    {
        var store = new RecordingSettingsStore(new AppSettings());
        var coordinator = CreateCoordinator(store, out _, out _);

        coordinator.Flush();

        store.UpdateCalls.Must().Be(0);
        store.SaveCalls.Must().Be(0);
    }

    [Fact]
    public void Flush_shouldPersistPendingMutationImmediatelyViaMergeUpdate()
    {
        var store = new RecordingSettingsStore(new AppSettings { Theme = ThemeMode.Light });
        var coordinator = CreateCoordinator(store, out _, out var scheduler);

        coordinator.ScheduleSave(settings => settings.SidebarWidth = 320);
        coordinator.Flush();
        scheduler.Fire();

        store.UpdateCalls.Must().Be(1);
        store.SaveCalls.Must().Be(0);
        store.Current.SidebarWidth.Must().Be(320);
        store.Current.Theme.Must().Be(ThemeMode.Light);
    }

    [Fact]
    public void ScheduleSave_shouldPersistViaMergeUpdate_whenDebounceElapses()
    {
        var store = new RecordingSettingsStore(new AppSettings { Theme = ThemeMode.Light });
        var coordinator = CreateCoordinator(store, out _, out var scheduler);

        coordinator.ScheduleSave(settings => settings.SidebarWidth = 320);
        scheduler.Fire();

        scheduler.LastDelay.Must().Be(AppSettingsCoordinator.PersistDebounceDelay);
        store.UpdateCalls.Must().Be(1);
        store.SaveCalls.Must().Be(0);
        store.Current.SidebarWidth.Must().Be(320);
        store.Current.Theme.Must().Be(ThemeMode.Light);
    }

    [Fact]
    public void ScheduleSave_shouldCoalesceRapidMutationsIntoOneMergeUpdate()
    {
        var store = new RecordingSettingsStore(new AppSettings { Theme = ThemeMode.Light });
        var coordinator = CreateCoordinator(store, out _, out var scheduler);

        coordinator.ScheduleSave(settings => settings.SidebarWidth = 300);
        coordinator.ScheduleSave(settings => settings.Theme = ThemeMode.Dark);
        scheduler.Fire();

        store.UpdateCalls.Must().Be(1);
        store.SaveCalls.Must().Be(0);
        store.Current.SidebarWidth.Must().Be(300);
        store.Current.Theme.Must().Be(ThemeMode.Dark);
    }

    [Fact]
    public void SaveNow_AppliesTheme_WhenThemeChanges()
    {
        var store = new RecordingSettingsStore(new AppSettings { Theme = ThemeMode.Light });
        var coordinator = CreateCoordinator(store, out var theme, out _);

        coordinator.SaveNow(settings => settings.Theme = ThemeMode.Dark);

        theme.Applied.Must().Contain(ThemeMode.Dark);
    }

    private static AppSettingsCoordinator CreateCoordinator(
        RecordingSettingsStore store,
        out RecordingThemeService theme,
        out ManualDebounceScheduler scheduler)
    {
        theme = new RecordingThemeService();
        scheduler = new ManualDebounceScheduler();
        return new AppSettingsCoordinator(
            store,
            theme,
            new NoopUiFontService(),
            new NoopAccentBrushService(),
            scheduler.Schedule);
    }

    private sealed class RecordingSettingsStore(AppSettings initial) : ISettingsStore
    {
        private int _saveCalls;
        private int _updateCalls;

        public AppSettings Current { get; private set; } = initial;
        public int SaveCalls => Volatile.Read(ref _saveCalls);
        public int UpdateCalls => Volatile.Read(ref _updateCalls);

        public AppSettings Load() => Current;

        public void Save(AppSettings settings)
        {
            Interlocked.Increment(ref _saveCalls);
            Current = settings;
        }

        public void Update(Action<AppSettings> mutate)
        {
            Interlocked.Increment(ref _updateCalls);
            var next = Current;
            mutate(next);
            Current = next;
        }
    }

    private sealed class RecordingThemeService : IThemeService
    {
        private readonly List<ThemeMode> _applied = new();

        public IReadOnlyList<ThemeMode> Applied => _applied;
        public ThemeMode CurrentMode { get; private set; } = ThemeMode.System;

        public void ApplyTheme(ThemeMode mode)
        {
            _applied.Add(mode);
            CurrentMode = mode;
        }

        public event Action<ThemeMode>? ThemeChanged { add { } remove { } }
    }

    private sealed class NoopUiFontService : IUiFontService
    {
        public UiFontFamily Current => UiFontFamily.System;
        public void ApplyFont(UiFontFamily font) { }
    }

    private sealed class NoopAccentBrushService : IAccentBrushService
    {
        public uint? CustomAccentArgb => null;
        public void ApplyCustomAccent(uint? argb) { }
        public event Action? AccentChanged { add { } remove { } }
    }

    private sealed class ManualDebounceScheduler
    {
        private readonly List<(Action Callback, CancellationToken Token)> _scheduled = [];

        public TimeSpan LastDelay { get; private set; }

        public void Schedule(TimeSpan delay, Action callback, CancellationToken cancellationToken)
        {
            LastDelay = delay;
            _scheduled.Add((callback, cancellationToken));
        }

        public void Fire()
        {
            var pending = _scheduled.ToArray();
            _scheduled.Clear();

            foreach (var (callback, token) in pending)
            {
                if (!token.IsCancellationRequested)
                    callback();
            }
        }
    }
}
