using HelixExplorer.Core.Models;
using HelixExplorer.Core.Settings;
using HelixExplorer.Services;

namespace HelixExplorer.ViewModels.Tests;

public class FolderViewPreferencesServiceTests
{
    [Fact]
    public void Set_ThenTryGet_ReturnsOverride_ForNormalizedPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "helix-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new JsonSettingsStore(path);
            store.Save(new AppSettings());
            var service = new FolderViewPreferencesService(store);

            service.Set(@"C:\Users\docs\", new FolderViewPreferences
            {
                ViewMode = LayoutMode.Grid,
                SortColumn = SortColumn.Size,
                SortDescending = true,
                ThumbnailSize = 128
            });

            service.TryGet(@"C:\Users\docs", out var prefs).Must().BeTrue();
            prefs.ViewMode.Must().Be(LayoutMode.Grid);
            prefs.SortColumn.Must().Be(SortColumn.Size);
            prefs.SortDescending.Must().BeTrue();
            prefs.ThumbnailSize.Must().Be(128);

            var reloaded = new FolderViewPreferencesService(store);
            reloaded.TryGet(@"C:\Users\docs\", out var persisted).Must().BeTrue();
            persisted.ViewMode.Must().Be(LayoutMode.Grid);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Set_ThenReload_RoundTripsGroupingState()
    {
        var path = Path.Combine(Path.GetTempPath(), "helix-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new JsonSettingsStore(path);
            store.Save(new AppSettings());
            var service = new FolderViewPreferencesService(store);

            service.Set(@"C:\Users\pics", new FolderViewPreferences
            {
                ViewMode = LayoutMode.Grid,
                GroupBy = GroupByMode.Modified,
                CollapsedGroupKeys = ["modified_today", "modified_last_week"]
            });

            // Reload from disk: grouping must survive JSON serialisation, not just the in-memory cache.
            var reloaded = new FolderViewPreferencesService(store);
            reloaded.TryGet(@"C:\Users\pics", out var prefs).Must().BeTrue();
            prefs.GroupBy.Must().Be(GroupByMode.Modified);
            prefs.CollapsedGroupKeys.Must().BeSequenceEqual(new[] { "modified_today", "modified_last_week" });
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Defaults_AreUngroupedWithNoCollapsedKeys()
    {
        var path = Path.Combine(Path.GetTempPath(), "helix-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new JsonSettingsStore(path);
            store.Save(new AppSettings());
            var service = new FolderViewPreferencesService(store);

            service.Set(@"C:\Users\plain", new FolderViewPreferences { ViewMode = LayoutMode.Grid });

            service.TryGet(@"C:\Users\plain", out var prefs).Must().BeTrue();
            prefs.GroupBy.Must().Be(GroupByMode.None);
            prefs.CollapsedGroupKeys.Must().BeEmpty();
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void CollapsedGroupKeys_AreClonedOnStoreAndFetch()
    {
        var path = Path.Combine(Path.GetTempPath(), "helix-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new JsonSettingsStore(path);
            store.Save(new AppSettings());
            var service = new FolderViewPreferencesService(store);

            var keys = new List<string> { "type_folder" };
            service.Set(@"C:\Users\clone", new FolderViewPreferences
            {
                GroupBy = GroupByMode.Type,
                CollapsedGroupKeys = keys
            });

            // Mutating the caller's list (or the returned copy) must not reach stored state.
            keys.Add("type_zip");
            service.TryGet(@"C:\Users\clone", out var first).Must().BeTrue();
            first.CollapsedGroupKeys.Must().BeSequenceEqual(new[] { "type_folder" });

            first.CollapsedGroupKeys.Add("type_md");
            service.TryGet(@"C:\Users\clone", out var second).Must().BeTrue();
            second.CollapsedGroupKeys.Must().BeSequenceEqual(new[] { "type_folder" });
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void TryGet_False_WhenNoOverride()
    {
        var path = Path.Combine(Path.GetTempPath(), "helix-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new JsonSettingsStore(path);
            store.Save(new AppSettings());
            var service = new FolderViewPreferencesService(store);

            service.TryGet(@"C:\Users\unknown", out _).Must().BeFalse();
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
