using HelixExplorer.Core.Models;
using HelixExplorer.Core.Persistence;
using HelixExplorer.Core.Settings;
using HelixExplorer.Services;

namespace HelixExplorer.ViewModels.Tests;

public class FolderViewPreferencesServiceTests
{
    private static string NewTempDbPath()
        => Path.Combine(Path.GetTempPath(), "helix-test-" + Guid.NewGuid().ToString("N") + ".db");

    private static string NewTempSettingsPath()
        => Path.Combine(Path.GetTempPath(), "helix-settings-" + Guid.NewGuid().ToString("N") + ".json");

    private static void CleanupDb(string path)
    {
        foreach (var p in new[] { path, path + "-wal", path + "-shm" })
        {
            try { File.Delete(p); } catch { }
        }
    }

    private static (SqliteAppDatabase db, FolderViewPreferencesStore store, FolderViewPreferencesService service) CreateService(string dbPath)
    {
        var settingsPath = NewTempSettingsPath();
        var db = new SqliteAppDatabase(new JsonSettingsStore(settingsPath), dbPath, settingsPath);
        db.Initialize();
        var store = new FolderViewPreferencesStore(db);
        return (db, store, new FolderViewPreferencesService(store));
    }

    [Fact]
    public void Set_ThenTryGet_ReturnsOverride_ForNormalizedPath()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, store, service) = CreateService(dbPath);
            using (db)
            {
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

                // Reload from disk via a fresh service backed by the same DB: persistence must survive.
                var reloadedStore = new FolderViewPreferencesStore(db);
                var reloaded = new FolderViewPreferencesService(reloadedStore);
                reloaded.TryGet(@"C:\Users\docs\", out var persisted).Must().BeTrue();
                persisted.ViewMode.Must().Be(LayoutMode.Grid);
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void Set_ThenReload_RoundTripsGroupingState()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, _, service) = CreateService(dbPath);
            using (db)
            {
                service.Set(@"C:\Users\pics", new FolderViewPreferences
                {
                    ViewMode = LayoutMode.Grid,
                    GroupBy = GroupByMode.Modified,
                    CollapsedGroupKeys = ["modified_today", "modified_last_week"]
                });

                // Reload from DB: grouping must survive SQLite round-trip, not just the in-memory cache.
                var reloadedStore = new FolderViewPreferencesStore(db);
                var reloaded = new FolderViewPreferencesService(reloadedStore);
                reloaded.TryGet(@"C:\Users\pics", out var prefs).Must().BeTrue();
                prefs.GroupBy.Must().Be(GroupByMode.Modified);
                prefs.CollapsedGroupKeys.Must().BeSequenceEqual(new[] { "modified_today", "modified_last_week" });
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void Defaults_AreUngroupedWithNoCollapsedKeys()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, _, service) = CreateService(dbPath);
            using (db)
            {
                service.Set(@"C:\Users\plain", new FolderViewPreferences { ViewMode = LayoutMode.Grid });

                service.TryGet(@"C:\Users\plain", out var prefs).Must().BeTrue();
                prefs.GroupBy.Must().Be(GroupByMode.None);
                prefs.CollapsedGroupKeys.Must().BeEmpty();
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void CollapsedGroupKeys_AreClonedOnStoreAndFetch()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, _, service) = CreateService(dbPath);
            using (db)
            {
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
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void TryGet_False_WhenNoOverride()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, _, service) = CreateService(dbPath);
            using (db)
                service.TryGet(@"C:\Users\unknown", out _).Must().BeFalse();
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void Remove_DeletesOverride_FromCacheAndStore()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, store, service) = CreateService(dbPath);
            using (db)
            {
                service.Set(@"C:\Users\removeme", new FolderViewPreferences { ViewMode = LayoutMode.Grid });
                service.Remove(@"C:\Users\removeme");

                // Cache miss + store miss.
                service.TryGet(@"C:\Users\removeme", out _).Must().BeFalse();
                store.TryGet(@"C:\Users\removeme", out _).Must().BeFalse();
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }
}
