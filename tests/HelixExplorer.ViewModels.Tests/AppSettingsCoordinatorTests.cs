using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Theming;

namespace HelixExplorer.ViewModels.Tests;

public class AppSettingsCoordinatorTests
{
    [Fact]
    public void SaveNow_PersistsViaMergeUpdate_NotWholeDocumentSave()
    {
        var store = new RecordingSettingsStore(new AppSettings { Theme = ThemeMode.Light });
        var coordinator = CreateCoordinator(store, out _);

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
        var coordinator = CreateCoordinator(store, out _);

        coordinator.Flush();

        store.UpdateCalls.Must().Be(0);
        store.SaveCalls.Must().Be(0);
    }

    [Fact]
    public void Flush_PersistsPendingMutation_Immediately_ViaMergeUpdate()
    {
        using var sync = new SyncContextScope();
        var store = new RecordingSettingsStore(new AppSettings { Theme = ThemeMode.Light });
        var coordinator = CreateCoordinator(store, out _);

        coordinator.ScheduleSave(settings => settings.SidebarWidth = 320);
        coordinator.Flush();

        store.UpdateCalls.Must().Be(1);
        store.SaveCalls.Must().Be(0);
        store.Current.SidebarWidth.Must().Be(320);
        store.Current.Theme.Must().Be(ThemeMode.Light);
    }

    [Fact]
    public async Task ScheduleSave_PersistsAfterDebounce_ViaMergeUpdate()
    {
        using var sync = new SyncContextScope();
        var store = new RecordingSettingsStore(new AppSettings { Theme = ThemeMode.Light });
        var coordinator = CreateCoordinator(store, out _);

        coordinator.ScheduleSave(settings => settings.SidebarWidth = 320);

        await Task.Delay(500);

        store.UpdateCalls.Must().Be(1);
        store.SaveCalls.Must().Be(0);
        store.Current.SidebarWidth.Must().Be(320);
        store.Current.Theme.Must().Be(ThemeMode.Light);
    }

    [Fact]
    public async Task ScheduleSave_CoalescesRapidMutations_IntoOneMergeUpdate()
    {
        using var sync = new SyncContextScope();
        var store = new RecordingSettingsStore(new AppSettings { Theme = ThemeMode.Light });
        var coordinator = CreateCoordinator(store, out _);

        coordinator.ScheduleSave(settings => settings.SidebarWidth = 300);
        coordinator.ScheduleSave(settings => settings.Theme = ThemeMode.Dark);

        await Task.Delay(500);

        store.UpdateCalls.Must().Be(1);
        store.SaveCalls.Must().Be(0);
        store.Current.SidebarWidth.Must().Be(300);
        store.Current.Theme.Must().Be(ThemeMode.Dark);
    }

    [Fact]
    public void SaveNow_AppliesTheme_WhenThemeChanges()
    {
        var store = new RecordingSettingsStore(new AppSettings { Theme = ThemeMode.Light });
        var coordinator = CreateCoordinator(store, out var theme);

        coordinator.SaveNow(settings => settings.Theme = ThemeMode.Dark);

        theme.Applied.Must().Contain(ThemeMode.Dark);
    }

    private static AppSettingsCoordinator CreateCoordinator(
        RecordingSettingsStore store, out RecordingThemeService theme)
    {
        theme = new RecordingThemeService();
        return new AppSettingsCoordinator(
            store, theme, new NoopUiFontService(), new NoopAccentBrushService());
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

    private sealed class SyncContextScope : IDisposable
    {
        private readonly SynchronizationContext? _previous = SynchronizationContext.Current;

        public SyncContextScope()
            => SynchronizationContext.SetSynchronizationContext(new ImmediateSyncContext());

        public void Dispose()
            => SynchronizationContext.SetSynchronizationContext(_previous);
    }

    private sealed class ImmediateSyncContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
        public override void Send(SendOrPostCallback d, object? state) => d(state);
    }
}
