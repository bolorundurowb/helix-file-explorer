using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Theming;

namespace HelixExplorer.ViewModels.Tests;

public class AppSettingsCoordinatorTests
{
    [Fact]
    public void SaveNow_PersistsViaMergeUpdate_NotWholeDocumentSave()
    {
        var store = new RecordingSettingsStore(new AppSettings { Theme = ThemeMode.Light });
        var coordinator = new AppSettingsCoordinator(
            store, new NoopThemeService(), new NoopUiFontService(), new NoopAccentBrushService());

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
        var coordinator = new AppSettingsCoordinator(
            store, new NoopThemeService(), new NoopUiFontService(), new NoopAccentBrushService());

        coordinator.Flush();

        store.UpdateCalls.Must().Be(0);
        store.SaveCalls.Must().Be(0);
    }

    private sealed class RecordingSettingsStore(AppSettings initial) : ISettingsStore
    {
        public AppSettings Current { get; private set; } = initial;
        public int SaveCalls { get; private set; }
        public int UpdateCalls { get; private set; }

        public AppSettings Load() => Current;

        public void Save(AppSettings settings)
        {
            SaveCalls++;
            Current = settings;
        }

        public void Update(Action<AppSettings> mutate)
        {
            UpdateCalls++;
            var next = Current;
            mutate(next);
            Current = next;
        }
    }

    private sealed class NoopThemeService : IThemeService
    {
        public ThemeMode CurrentMode => ThemeMode.System;
        public void ApplyTheme(ThemeMode mode) { }
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
}
