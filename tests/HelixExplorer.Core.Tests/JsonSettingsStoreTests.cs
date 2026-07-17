using HelixExplorer.Core.Models;
using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Theming;

namespace HelixExplorer.Core.Tests;

public class JsonSettingsStoreTests
{
    [Fact]
    public void Save_ThenLoad_RoundTripsSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), "helix-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new JsonSettingsStore(path);
            store.Save(new AppSettings
            {
                Theme = ThemeMode.Dark,
                SidebarWidth = 320,
                DefaultViewMode = LayoutMode.Grid
            });

            var loaded = store.Load();
            loaded.Theme.Must().Be(ThemeMode.Dark);
            loaded.SidebarWidth.Must().Be(320);
            loaded.DefaultViewMode.Must().Be(LayoutMode.Grid);
            File.Exists(path + ".tmp").Must().BeFalse();
        }
        finally
        {
            try { File.Delete(path); } catch { }
            try { File.Delete(path + ".tmp"); } catch { }
        }
    }

    [Fact]
    public void Save_LeavesExistingFileIntact_WhenTempWriteWouldCorrupt()
    {
        var path = Path.Combine(Path.GetTempPath(), "helix-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new JsonSettingsStore(path);
            store.Save(new AppSettings { Theme = ThemeMode.Light, SidebarWidth = 200 });

            File.WriteAllText(path + ".tmp", "{INVALID");

            var loaded = store.Load();
            loaded.Theme.Must().Be(ThemeMode.Light);
            loaded.SidebarWidth.Must().Be(200);
        }
        finally
        {
            try { File.Delete(path); } catch { }
            try { File.Delete(path + ".tmp"); } catch { }
        }
    }

    [Fact]
    public void Save_OverwritesPreviousSettingsAtomically()
    {
        var path = Path.Combine(Path.GetTempPath(), "helix-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new JsonSettingsStore(path);
            store.Save(new AppSettings { Theme = ThemeMode.Light });
            store.Save(new AppSettings { Theme = ThemeMode.Dark, AccentColorArgb = 0xFF0078D4 });

            var loaded = store.Load();
            loaded.Theme.Must().Be(ThemeMode.Dark);
            loaded.AccentColorArgb.Must().Be(0xFF0078D4u);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
