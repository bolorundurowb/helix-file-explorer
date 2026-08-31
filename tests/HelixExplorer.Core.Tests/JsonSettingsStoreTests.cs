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

    [Fact]
    public void Load_SettingsFileWithoutNewTabPreference_KeepsSwitchingToNewTabs()
    {
        var path = Path.Combine(Path.GetTempPath(), "helix-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            // A file written before the preference existed must not silently change tab behaviour.
            File.WriteAllText(path, "{\"Theme\":\"Dark\",\"SidebarWidth\":260}");

            var loaded = new JsonSettingsStore(path).Load();

            loaded.SwitchToNewTabOnOpen.Must().BeTrue();
            loaded.SidebarWidth.Must().Be(260);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Load_CorruptJson_QuarantinesFileAndReturnsDefaults()
    {
        // CORE-5: previously returned defaults with zero trace and left the corrupt file where the
        // next Save() would silently overwrite it - no evidence anything had gone wrong ever existed.
        var path = Path.Combine(Path.GetTempPath(), "helix-settings-" + Guid.NewGuid().ToString("N") + ".json");
        var directory = Path.GetDirectoryName(path)!;
        var fileName = Path.GetFileName(path);
        try
        {
            File.WriteAllText(path, "{ not actually json");

            var loaded = new JsonSettingsStore(path).Load();

            loaded.Theme.Must().Be(ThemeMode.System);
            File.Exists(path).Must().BeFalse();
            Directory.EnumerateFiles(directory, fileName + ".corrupt-*").Any().Must().BeTrue();
        }
        finally
        {
            try { File.Delete(path); } catch { }
            foreach (var leftover in Directory.EnumerateFiles(directory, fileName + ".corrupt-*"))
            {
                try { File.Delete(leftover); } catch { }
            }
        }
    }

    [Fact]
    public async Task Save_ConcurrentCalls_DoNotThrowAndLeaveValidJson()
    {
        var path = Path.Combine(Path.GetTempPath(), "helix-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new JsonSettingsStore(path);
            var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(() =>
            {
                store.Save(new AppSettings
                {
                    Theme = i % 2 == 0 ? ThemeMode.Dark : ThemeMode.Light,
                    SidebarWidth = 200 + i
                });
            }));

            await Task.WhenAll(tasks);

            var loaded = store.Load();
            loaded.SidebarWidth.Must().BeGreaterThan(199);
            File.Exists(path).Must().BeTrue();
        }
        finally
        {
            try { File.Delete(path); } catch { }
            foreach (var leftover in Directory.EnumerateFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"))
            {
                try { File.Delete(leftover); } catch { }
            }
        }
    }
}
