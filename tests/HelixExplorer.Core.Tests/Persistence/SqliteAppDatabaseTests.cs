using System.Text.Json;
using System.Text.Json.Serialization;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Persistence;
using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Theming;

namespace HelixExplorer.Core.Tests.Persistence;

public class SqliteAppDatabaseTests
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

    [Fact]
    public void Initialize_CreatesSchemaTables()
    {
        var dbPath = NewTempDbPath();
        var settingsPath = NewTempSettingsPath();
        try
        {
            using var db = new SqliteAppDatabase(new JsonSettingsStore(settingsPath), dbPath, settingsPath);
            db.Initialize();

            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT name FROM sqlite_master
                WHERE type = 'table' AND name IN ('folder_view_preferences', 'folder_colors')
                ORDER BY name;
                """;
            using var reader = cmd.ExecuteReader();
            var names = new List<string>();
            while (reader.Read())
                names.Add(reader.GetString(0));

            names.Must().BeSequenceEqual(new[] { "folder_colors", "folder_view_preferences" });
        }
        finally
        {
            CleanupDb(dbPath);
            try { File.Delete(settingsPath); } catch { }
        }
    }

    [Fact]
    public void Initialize_SetsUserVersionToOne()
    {
        var dbPath = NewTempDbPath();
        var settingsPath = NewTempSettingsPath();
        try
        {
            using var db = new SqliteAppDatabase(new JsonSettingsStore(settingsPath), dbPath, settingsPath);
            db.Initialize();

            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            var version = Convert.ToInt32(cmd.ExecuteScalar() ?? 0, System.Globalization.CultureInfo.InvariantCulture);
            version.Must().Be(1);
        }
        finally
        {
            CleanupDb(dbPath);
            try { File.Delete(settingsPath); } catch { }
        }
    }

    [Fact]
    public void Initialize_IsIdempotent()
    {
        var dbPath = NewTempDbPath();
        var settingsPath = NewTempSettingsPath();
        try
        {
            using var db = new SqliteAppDatabase(new JsonSettingsStore(settingsPath), dbPath, settingsPath);
            db.Initialize();
            db.Initialize();
            // No exception means success.
            true.Must().BeTrue();
        }
        finally
        {
            CleanupDb(dbPath);
            try { File.Delete(settingsPath); } catch { }
        }
    }
}

public class FolderViewPreferencesStoreTests
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

    private static (SqliteAppDatabase db, FolderViewPreferencesStore store) CreateStore(string dbPath)
    {
        var settingsPath = NewTempSettingsPath();
        var db = new SqliteAppDatabase(new JsonSettingsStore(settingsPath), dbPath, settingsPath);
        db.Initialize();
        return (db, new FolderViewPreferencesStore(db));
    }

    [Fact]
    public void Upsert_ThenTryGet_ReturnsStoredValues()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, store) = CreateStore(dbPath);
            using (db)
            {
                store.Upsert(@"C:\Users\docs", new FolderViewPreferences
                {
                    ViewMode = LayoutMode.Grid,
                    SortColumn = SortColumn.Size,
                    SortDescending = true,
                    DirectorySort = DirectorySortMode.FoldersFirst,
                    ThumbnailSize = 128,
                    GroupBy = GroupByMode.Modified
                });

                store.TryGet(@"C:\Users\docs", out var prefs).Must().BeTrue();
                prefs.ViewMode.Must().Be(LayoutMode.Grid);
                prefs.SortColumn.Must().Be(SortColumn.Size);
                prefs.SortDescending.Must().BeTrue();
                prefs.DirectorySort.Must().Be(DirectorySortMode.FoldersFirst);
                prefs.ThumbnailSize.Must().Be(128);
                prefs.GroupBy.Must().Be(GroupByMode.Modified);
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void TryGet_False_WhenPathNotStored()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, store) = CreateStore(dbPath);
            using (db)
                store.TryGet(@"C:\Users\unknown", out _).Must().BeFalse();
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void TryGet_IsCaseInsensitive()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, store) = CreateStore(dbPath);
            using (db)
            {
                store.Upsert(@"C:\Users\Docs", new FolderViewPreferences { ViewMode = LayoutMode.Grid });

                store.TryGet(@"c:\users\docs", out var prefs).Must().BeTrue();
                prefs.ViewMode.Must().Be(LayoutMode.Grid);
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void CollapsedGroupKeys_RoundTrip()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, store) = CreateStore(dbPath);
            using (db)
            {
                var keys = new List<string> { "modified_today", "modified_last_week", "type_folder" };
                store.Upsert(@"C:\Users\pics", new FolderViewPreferences
                {
                    GroupBy = GroupByMode.Modified,
                    CollapsedGroupKeys = keys
                });

                store.TryGet(@"C:\Users\pics", out var prefs).Must().BeTrue();
                prefs.CollapsedGroupKeys.Must().BeSequenceEqual(keys);
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void CollapsedGroupKeys_DefaultsToEmpty_WhenNotSet()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, store) = CreateStore(dbPath);
            using (db)
            {
                store.Upsert(@"C:\Users\plain", new FolderViewPreferences { ViewMode = LayoutMode.Details });

                store.TryGet(@"C:\Users\plain", out var prefs).Must().BeTrue();
                prefs.CollapsedGroupKeys.Must().BeEmpty();
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void Delete_RemovesRow()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, store) = CreateStore(dbPath);
            using (db)
            {
                store.Upsert(@"C:\Users\todelete", new FolderViewPreferences { ViewMode = LayoutMode.Grid });
                store.Delete(@"C:\Users\todelete");

                store.TryGet(@"C:\Users\todelete", out _).Must().BeFalse();
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void Upsert_OverwritesExistingRow()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, store) = CreateStore(dbPath);
            using (db)
            {
                store.Upsert(@"C:\Users\docs", new FolderViewPreferences { ViewMode = LayoutMode.Details, ThumbnailSize = 72 });
                store.Upsert(@"C:\Users\docs", new FolderViewPreferences { ViewMode = LayoutMode.Grid, ThumbnailSize = 96 });

                store.TryGet(@"C:\Users\docs", out var prefs).Must().BeTrue();
                prefs.ViewMode.Must().Be(LayoutMode.Grid);
                prefs.ThumbnailSize.Must().Be(96);
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public async Task ConcurrentWrites_UnderLock_DoNotThrowAndPersistAll()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, store) = CreateStore(dbPath);
            using (db)
            {
                var paths = Enumerable.Range(0, 20)
                    .Select(i => $@"C:\Users\folder{i}")
                    .ToList();

                var tasks = paths.Select(p => Task.Run(() =>
                    store.Upsert(p, new FolderViewPreferences
                    {
                        ViewMode = LayoutMode.Grid,
                        SortColumn = SortColumn.Name,
                        ThumbnailSize = 96
                    })));

                await Task.WhenAll(tasks);

                foreach (var p in paths)
                {
                    store.TryGet(p, out var prefs).Must().BeTrue();
                    prefs.ViewMode.Must().Be(LayoutMode.Grid);
                }
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }
}

public class FolderColorStoreTests
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

    private static (SqliteAppDatabase db, FolderColorStore store) CreateStore(string dbPath)
    {
        var settingsPath = NewTempSettingsPath();
        var db = new SqliteAppDatabase(new JsonSettingsStore(settingsPath), dbPath, settingsPath);
        db.Initialize();
        return (db, new FolderColorStore(db));
    }

    [Fact]
    public void LoadAll_ReturnsEmpty_WhenNoRows()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, store) = CreateStore(dbPath);
            using (db)
                store.LoadAll().Count.Must().Be(0);
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void Upsert_ThenLoadAll_ReturnsColor()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, store) = CreateStore(dbPath);
            using (db)
            {
                store.Upsert(@"C:\Users\docs", 0xFFFF0000);
                store.Upsert(@"C:\Users\pics", 0xFF0078D4);

                var all = store.LoadAll();
                all.Count.Must().Be(2);
                all[@"C:\Users\docs"].Must().Be(0xFFFF0000u);
                all[@"C:\Users\pics"].Must().Be(0xFF0078D4u);
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void LoadAll_IsCaseInsensitive()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, store) = CreateStore(dbPath);
            using (db)
            {
                store.Upsert(@"C:\Users\Docs", 0xFF112233);

                var all = store.LoadAll();
                all[@"c:\users\docs"].Must().Be(0xFF112233u);
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void Upsert_OverwritesExistingColor()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, store) = CreateStore(dbPath);
            using (db)
            {
                store.Upsert(@"C:\Users\docs", 0xFF0000FF);
                store.Upsert(@"C:\Users\docs", 0xFFFF0000);

                var all = store.LoadAll();
                all[@"C:\Users\docs"].Must().Be(0xFFFF0000u);
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void Delete_RemovesColor()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, store) = CreateStore(dbPath);
            using (db)
            {
                store.Upsert(@"C:\Users\docs", 0xFF00FF00);
                store.Delete(@"C:\Users\docs");

                store.LoadAll().Count.Must().Be(0);
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }
}

public class LegacyJsonMigrationTests
{
    private static readonly JsonSerializerOptions LegacyOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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

    private static void WriteLegacySettings(string path, Dictionary<string, FolderViewPreferences> prefs, Dictionary<string, uint> colors)
    {
        // Simulate a pre-migration settings.json that still contains the map keys.
        var json = JsonSerializer.Serialize(new
        {
            Theme = "Dark",
            SidebarWidth = 240,
            FolderViewPreferences = prefs,
            FolderColors = colors
        }, LegacyOptions);
        File.WriteAllText(path, json);
    }

    [Fact]
    public void Initialize_MigratesLegacyJson_ToDatabase()
    {
        var dbPath = NewTempDbPath();
        var settingsPath = NewTempSettingsPath();
        try
        {
            WriteLegacySettings(settingsPath,
                new() { [@"C:\Users\docs"] = new() { ViewMode = LayoutMode.Grid, ThumbnailSize = 96 } },
                new() { [@"C:\Users\docs"] = 0xFF0078D4 });

            var settingsStore = new JsonSettingsStore(settingsPath);
            using var db = new SqliteAppDatabase(settingsStore, dbPath, settingsPath);
            db.Initialize();

            var prefStore = new FolderViewPreferencesStore(db);
            prefStore.TryGet(@"C:\Users\docs", out var prefs).Must().BeTrue();
            prefs.ViewMode.Must().Be(LayoutMode.Grid);
            prefs.ThumbnailSize.Must().Be(96);

            var colorStore = new FolderColorStore(db);
            var colors = colorStore.LoadAll();
            colors[@"C:\Users\docs"].Must().Be(0xFF0078D4u);
        }
        finally
        {
            CleanupDb(dbPath);
            try { File.Delete(settingsPath); } catch { }
        }
    }

    [Fact]
    public void Initialize_RewritesSettingsJson_WithoutLegacyMaps()
    {
        var dbPath = NewTempDbPath();
        var settingsPath = NewTempSettingsPath();
        try
        {
            WriteLegacySettings(settingsPath,
                new() { [@"C:\Users\docs"] = new() { ViewMode = LayoutMode.Grid } },
                new() { [@"C:\Users\docs"] = 0xFF0078D4 });

            var settingsStore = new JsonSettingsStore(settingsPath);
            using var db = new SqliteAppDatabase(settingsStore, dbPath, settingsPath);
            db.Initialize();

            var rewritten = File.ReadAllText(settingsPath);
            rewritten.Must().NotContain("FolderViewPreferences");
            rewritten.Must().NotContain("FolderColors");
            // Chrome values survive the rewrite.
            var reloaded = settingsStore.Load();
            reloaded.Theme.Must().Be(ThemeMode.Dark);
            reloaded.SidebarWidth.Must().Be(240);
        }
        finally
        {
            CleanupDb(dbPath);
            try { File.Delete(settingsPath); } catch { }
        }
    }

    [Fact]
    public void Initialize_DoesNotOverwriteExistingDbRows_WhenLegacyJsonStillPresent()
    {
        var dbPath = NewTempDbPath();
        var settingsPath = NewTempSettingsPath();
        try
        {
            var settingsStore = new JsonSettingsStore(settingsPath);
            using (var db = new SqliteAppDatabase(settingsStore, dbPath, settingsPath))
            {
                db.Initialize();
                new FolderViewPreferencesStore(db).Upsert(
                    @"C:\Users\docs",
                    new FolderViewPreferences { ViewMode = LayoutMode.Details, ThumbnailSize = 72 });
                new FolderColorStore(db).Upsert(@"C:\Users\docs", 0xFF00FF00);
            }

            // Simulate a failed settings rewrite: JSON still has the old maps, but the DB
            // already has newer rows. Re-init must not copy JSON over them.
            WriteLegacySettings(settingsPath,
                new() { [@"C:\Users\docs"] = new() { ViewMode = LayoutMode.Grid, ThumbnailSize = 96 } },
                new() { [@"C:\Users\docs"] = 0xFF0078D4 });

            using var db2 = new SqliteAppDatabase(settingsStore, dbPath, settingsPath);
            db2.Initialize();

            var prefStore = new FolderViewPreferencesStore(db2);
            prefStore.TryGet(@"C:\Users\docs", out var prefs).Must().BeTrue();
            prefs.ViewMode.Must().Be(LayoutMode.Details);
            prefs.ThumbnailSize.Must().Be(72);

            new FolderColorStore(db2).LoadAll()[@"C:\Users\docs"].Must().Be(0xFF00FF00u);
        }
        finally
        {
            CleanupDb(dbPath);
            try { File.Delete(settingsPath); } catch { }
        }
    }

    [Fact]
    public void Initialize_DoesNotDuplicateOrOverwrite_OnSecondStartup()
    {
        var dbPath = NewTempDbPath();
        var settingsPath = NewTempSettingsPath();
        try
        {
            WriteLegacySettings(settingsPath,
                new() { [@"C:\Users\docs"] = new() { ViewMode = LayoutMode.Grid, ThumbnailSize = 96 } },
                new() { [@"C:\Users\docs"] = 0xFF0078D4 });

            var settingsStore = new JsonSettingsStore(settingsPath);

            using (var db1 = new SqliteAppDatabase(settingsStore, dbPath, settingsPath))
            {
                db1.Initialize();
            }

            // Second startup: settings.json no longer has the maps; DB already has the rows.
            // Initialize must not overwrite DB rows with empty JSON maps.
            using var db2 = new SqliteAppDatabase(settingsStore, dbPath, settingsPath);
            db2.Initialize();

            var prefStore = new FolderViewPreferencesStore(db2);
            prefStore.TryGet(@"C:\Users\docs", out var prefs).Must().BeTrue();
            prefs.ViewMode.Must().Be(LayoutMode.Grid);
            prefs.ThumbnailSize.Must().Be(96);

            var colorStore = new FolderColorStore(db2);
            colorStore.LoadAll().Count.Must().Be(1);
        }
        finally
        {
            CleanupDb(dbPath);
            try { File.Delete(settingsPath); } catch { }
        }
    }

    [Fact]
    public void Initialize_SkipsMigration_WhenSettingsFileMissing()
    {
        var dbPath = NewTempDbPath();
        var settingsPath = NewTempSettingsPath();
        try
        {
            // No settings.json exists.
            using var db = new SqliteAppDatabase(new JsonSettingsStore(settingsPath), dbPath, settingsPath);
            db.Initialize();

            var colorStore = new FolderColorStore(db);
            colorStore.LoadAll().Count.Must().Be(0);

            var prefStore = new FolderViewPreferencesStore(db);
            prefStore.TryGet(@"C:\Users\anything", out _).Must().BeFalse();
        }
        finally
        {
            CleanupDb(dbPath);
            try { File.Delete(settingsPath); } catch { }
        }
    }

    [Fact]
    public void Initialize_SkipsMigration_WhenLegacyMapsEmpty()
    {
        var dbPath = NewTempDbPath();
        var settingsPath = NewTempSettingsPath();
        try
        {
            // settings.json exists but has no legacy map entries.
            var settingsStore = new JsonSettingsStore(settingsPath);
            settingsStore.Save(new AppSettings { Theme = ThemeMode.Light });

            using var db = new SqliteAppDatabase(settingsStore, dbPath, settingsPath);
            db.Initialize();

            var colorStore = new FolderColorStore(db);
            colorStore.LoadAll().Count.Must().Be(0);

            var prefStore = new FolderViewPreferencesStore(db);
            prefStore.TryGet(@"C:\Users\anything", out _).Must().BeFalse();
        }
        finally
        {
            CleanupDb(dbPath);
            try { File.Delete(settingsPath); } catch { }
        }
    }
}
