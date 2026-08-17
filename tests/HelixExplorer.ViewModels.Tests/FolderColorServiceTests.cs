using HelixExplorer.Core.Persistence;
using HelixExplorer.Core.Settings;
using HelixExplorer.Services;

namespace HelixExplorer.ViewModels.Tests;

public class FolderColorServiceTests
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

    private static (SqliteAppDatabase db, FolderColorService service) CreateService(string dbPath)
    {
        var settingsPath = NewTempSettingsPath();
        var db = new SqliteAppDatabase(new JsonSettingsStore(settingsPath), dbPath, settingsPath);
        db.Initialize();
        var store = new FolderColorStore(db);
        return (db, new FolderColorService(store));
    }

    [Fact]
    public void SetColor_ThenTryGet_ReturnsColor()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, service) = CreateService(dbPath);
            using (db)
            {
                service.SetColor(@"C:\Users\docs", 0xFF0078D4);

                service.TryGetColor(@"C:\Users\docs", out var argb).Must().BeTrue();
                argb.Must().Be(0xFF0078D4u);
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void SetColor_PersistsAcrossReload()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, _) = CreateService(dbPath);
            using (db)
            {
                var store1 = new FolderColorStore(db);
                var svc1 = new FolderColorService(store1);
                svc1.SetColor(@"C:\Users\docs", 0xFFFF0000);
                svc1.SetColor(@"C:\Users\pics", 0xFF00FF00);

                // New service instance from the same DB: LoadAll must pick up persisted rows.
                var store2 = new FolderColorStore(db);
                var svc2 = new FolderColorService(store2);
                svc2.TryGetColor(@"C:\Users\docs", out var docs).Must().BeTrue();
                docs.Must().Be(0xFFFF0000u);
                svc2.TryGetColor(@"C:\Users\pics", out var pics).Must().BeTrue();
                pics.Must().Be(0xFF00FF00u);
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void TryGetColor_False_WhenNoColor()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, service) = CreateService(dbPath);
            using (db)
                service.TryGetColor(@"C:\Users\unknown", out _).Must().BeFalse();
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void SetColor_OverwritesExisting()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, service) = CreateService(dbPath);
            using (db)
            {
                service.SetColor(@"C:\Users\docs", 0xFF0000FF);
                service.SetColor(@"C:\Users\docs", 0xFFFF0000);

                service.TryGetColor(@"C:\Users\docs", out var argb).Must().BeTrue();
                argb.Must().Be(0xFFFF0000u);
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void RemoveColor_DeletesFromMemoryAndStore()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, service) = CreateService(dbPath);
            using (db)
            {
                service.SetColor(@"C:\Users\docs", 0xFF0078D4);
                service.RemoveColor(@"C:\Users\docs");

                service.TryGetColor(@"C:\Users\docs", out _).Must().BeFalse();

                // Verify the DB row is gone too.
                var store = new FolderColorStore(db);
                store.LoadAll().Count.Must().Be(0);
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void TryGetColor_NormalizesTrailingSeparator()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, service) = CreateService(dbPath);
            using (db)
            {
                service.SetColor(@"C:\Users\docs\", 0xFF0078D4);

                service.TryGetColor(@"C:\Users\docs", out var argb).Must().BeTrue();
                argb.Must().Be(0xFF0078D4u);
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void SetColor_RaisesColorsChanged()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, service) = CreateService(dbPath);
            using (db)
            {
                var raised = 0;
                service.ColorsChanged += (_, _) => raised++;

                service.SetColor(@"C:\Users\docs", 0xFF0078D4);
                raised.Must().Be(1);

                service.RemoveColor(@"C:\Users\docs");
                raised.Must().Be(2);
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void RemoveColor_DoesNotRaise_WhenPathNotFound()
    {
        var dbPath = NewTempDbPath();
        try
        {
            var (db, service) = CreateService(dbPath);
            using (db)
            {
                var raised = false;
                service.ColorsChanged += (_, _) => raised = true;

                service.RemoveColor(@"C:\Users\nonexistent");
                raised.Must().BeFalse();
            }
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }
}
