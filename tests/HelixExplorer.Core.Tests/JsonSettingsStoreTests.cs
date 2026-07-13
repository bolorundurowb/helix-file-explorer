using HelixExplorer.Core.Models;
using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Theming;
using Xunit;

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
            Assert.Equal(ThemeMode.Dark, loaded.Theme);
            Assert.Equal(320, loaded.SidebarWidth);
            Assert.Equal(LayoutMode.Grid, loaded.DefaultViewMode);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort */ }
            try { File.Delete(path + ".tmp"); } catch { /* best-effort */ }
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

            // Simulate a crash that left garbage in the temp sibling without completing Move.
            File.WriteAllText(path + ".tmp", "{INVALID");

            var loaded = store.Load();
            Assert.Equal(ThemeMode.Light, loaded.Theme);
            Assert.Equal(200, loaded.SidebarWidth);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort */ }
            try { File.Delete(path + ".tmp"); } catch { /* best-effort */ }
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
            Assert.Equal(ThemeMode.Dark, loaded.Theme);
            Assert.Equal(0xFF0078D4u, loaded.AccentColorArgb);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort */ }
        }
    }
}
