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
