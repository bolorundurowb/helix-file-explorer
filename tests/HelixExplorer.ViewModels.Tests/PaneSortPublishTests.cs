using System.Collections.Specialized;
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
/// Sort and group changes must reorder the live listing in place. The Details DataGrid ignores Move
/// notifications, so the reorder has to arrive as a Reset or the rows stay put until the folder reloads.
/// </summary>
public sealed class PaneSortPublishTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly PaneViewModel _pane;

    public PaneSortPublishTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "helix-sort-publish-" + Guid.NewGuid().ToString("N"));
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
    public void ChangingSortColumn_ReordersEntriesWithSingleReset()
    {
        Publish(File("alpha.txt", 300), File("beta.txt", 100), File("gamma.txt", 200));
        Names().Must().BeSequenceEqual(new[] { "alpha.txt", "beta.txt", "gamma.txt" });

        var actions = new List<NotifyCollectionChangedAction>();
        _pane.Entries.CollectionChanged += (_, e) => actions.Add(e.Action);

        _pane.SortColumn = SortColumn.Size;

        Names().Must().BeSequenceEqual(new[] { "beta.txt", "gamma.txt", "alpha.txt" });
        actions.Must().BeSequenceEqual(new[] { NotifyCollectionChangedAction.Reset });
    }

    [Fact]
    public void ChangingSortDirection_ReversesEntries()
    {
        Publish(File("alpha.txt"), File("beta.txt"), File("gamma.txt"));

        _pane.SortDescending = true;

        Names().Must().BeSequenceEqual(new[] { "gamma.txt", "beta.txt", "alpha.txt" });
    }

    [Fact]
    public void ChangingGroupBy_ReordersEntriesByBucket()
    {
        Publish(File("alpha.png"), File("beta.txt"), File("gamma.png"));

        _pane.GroupBy = GroupByMode.Type;

        var names = Names();
        // Type buckets keep same-extension files together regardless of name order.
        Math.Abs(names.IndexOf("alpha.png") - names.IndexOf("gamma.png")).Must().Be(1);
    }

    private void Publish(params FileSystemEntry[] entries)
        => ((IPaneRefreshHost)_pane).ApplySortAndPublish(new ListingPublishRequest
        {
            AllEntries = entries,
            GitSnapshot = GitStatusSnapshot.Empty,
            ShowHiddenFiles = true,
            ShowFileExtensions = true,
            IsFilterVisible = false,
            FilterText = string.Empty,
            SortColumn = _pane.SortColumn,
            SortDescending = _pane.SortDescending,
            DirectorySort = _pane.DirectorySort,
            GroupBy = _pane.GroupBy,
            GroupingUtcNow = Now
        });

    private List<string> Names() => _pane.Entries.Select(e => e.Name).ToList();

    private FileSystemEntry File(string name, long size = 0)
        => new(Path.Combine(_root, name), name, false, size, Now, Path.GetExtension(name));
}
