using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Persistence;
using HelixExplorer.Core.Settings;
using HelixExplorer.Services;
using HelixExplorer.ViewModels.Pane;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.ViewModels.Tests;

/// <summary>
/// The Recycle Bin is a shell namespace, not a file-system folder, so filter and search must narrow the
/// loaded listing (by name and pre-delete location) instead of being ignored or walking a <c>shell:</c> path.
/// </summary>
public sealed class PaneRecycleBinQueryTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly PaneViewModel _pane;

    public PaneRecycleBinQueryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "helix-bin-query-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var dbPath = Path.Combine(_root, "helix.db");
        var settingsPath = Path.Combine(_root, "settings.json");

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddHelixApplicationServices();
        services.AddSingleton<IAppDatabase>(_ =>
        {
            var db = new SqliteAppDatabase(new JsonSettingsStore(settingsPath), dbPath, settingsPath);
            db.Initialize();
            return db;
        });

        _provider = services.BuildServiceProvider(validateScopes: true);
        _scope = _provider.CreateScope();
        _pane = _scope.ServiceProvider.GetRequiredService<IPaneViewModelFactory>().Create();
    }

    public void Dispose()
    {
        _pane.Dispose();
        _scope.Dispose();
        _provider.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void RecycleBin_ShowsPathBreadcrumbWithoutBecomingFileSystem() => InRecycleBin(() =>
    {
        _pane.IsShellNamespace.Must().BeTrue();
        _pane.IsFileSystem.Must().BeFalse();
        _pane.IsPathMode.Must().BeTrue();
    });

    [Fact]
    public void Filter_NarrowsByDisplayName() => InRecycleBin(() =>
    {
        PublishBinEntries();

        _pane.EnterFilterMode();
        _pane.FilterText = "report";

        _pane.IsFilterMode.Must().BeTrue();
        Names().Must().BeSequenceEqual(new[] { "report.docx" });
    });

    [Fact]
    public void Filter_MatchesOriginalPath() => InRecycleBin(() =>
    {
        PublishBinEntries();

        _pane.EnterFilterMode();
        _pane.FilterText = "Invoices";

        Names().Must().BeSequenceEqual(new[] { "march.pdf" });
    });

    [Fact]
    public void Search_NarrowsLoadedListingByNameAndOriginalPath() => InRecycleBin(() =>
    {
        PublishBinEntries();

        _pane.EnterSearchMode();
        _pane.FilterText = "invoices";

        _pane.IsSearchMode.Must().BeTrue();
        Names().Must().BeSequenceEqual(new[] { "march.pdf" });

        _pane.FilterText = "report";
        Names().Must().BeSequenceEqual(new[] { "report.docx" });

        _pane.ExitSearchMode();
        Names().Count.Must().Be(3);
    });

    /// <summary>
    /// Entering the bin starts a real shell refresh whose publish is marshalled to the UI thread. Running the
    /// whole test as one UI-thread job keeps that publish from landing between the arrange and assert steps.
    /// </summary>
    private void InRecycleBin(Action body)
        => HeadlessSession.RunOnUiThread(() =>
        {
            _pane.CurrentPath = ShellPath.RecycleBin;
            body();
        });

    private void PublishBinEntries()
        => ((IPaneRefreshHost)_pane).ApplySortAndPublish(new ListingPublishRequest
        {
            AllEntries =
            [
                BinEntry("$R1.docx", "report.docx", @"C:\Users\me\Documents\report.docx"),
                BinEntry("$R2.pdf", "march.pdf", @"C:\Users\me\Invoices\march.pdf"),
                BinEntry("$R3.png", "photo.png", @"D:\Pictures\photo.png")
            ],
            GitSnapshot = GitStatusSnapshot.Empty,
            ShowHiddenFiles = true,
            ShowFileExtensions = true,
            IsFilterVisible = false,
            FilterText = string.Empty,
            SortColumn = SortColumn.Name,
            SortDescending = false,
            DirectorySort = DirectorySortMode.MixedWithFiles,
            GroupBy = GroupByMode.None,
            GroupingUtcNow = Now
        });

    private List<string> Names() => _pane.Entries.Select(e => e.Name).ToList();

    private static FileSystemEntry BinEntry(string storedName, string displayName, string originalPath)
        => new(
            @"C:\$Recycle.Bin\S-1-5-21\" + storedName,
            displayName,
            false,
            10,
            Now,
            Path.GetExtension(displayName),
            OriginalPath: originalPath);
}
