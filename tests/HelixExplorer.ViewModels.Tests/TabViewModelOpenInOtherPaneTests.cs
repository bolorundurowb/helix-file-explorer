using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Persistence;
using HelixExplorer.Core.Settings;
using HelixExplorer.Services;
using HelixExplorer.ViewModels;
using HelixExplorer.ViewModels.Pane;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.ViewModels.Tests;

public class TabViewModelOpenInOtherPaneTests : IDisposable
{
    private readonly string _root;
    private readonly string _parent;
    private readonly string _child;
    private readonly string _dbPath;
    private readonly string _settingsPath;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly TabViewModel _tab;

    public TabViewModelOpenInOtherPaneTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "helix-other-pane-" + Guid.NewGuid().ToString("N"));
        _parent = Path.Combine(_root, "parent");
        _child = Path.Combine(_parent, "child");
        Directory.CreateDirectory(_child);

        _dbPath = Path.Combine(_root, "helix.db");
        _settingsPath = Path.Combine(_root, "settings.json");

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddHelixApplicationServices();
        // Last registration wins: keep the real composition root but point persistence at a temp
        // profile so these tests do not touch the developer's helix.db.
        services.AddSingleton<IAppDatabase>(_ =>
        {
            var db = new SqliteAppDatabase(new JsonSettingsStore(_settingsPath), _dbPath, _settingsPath);
            db.Initialize();
            return db;
        });

        _provider = services.BuildServiceProvider(validateScopes: true);
        _scope = _provider.CreateScope();

        var sp = _scope.ServiceProvider;
        _tab = new TabViewModel(
            sp.GetRequiredService<IClipboardService>(),
            sp.GetRequiredService<IArchiveProvider>(),
            sp.GetRequiredService<IPaneViewModelFactory>(),
            sp.GetRequiredService<HomePageViewModel>());
    }

    public void Dispose()
    {
        _tab.Dispose();
        _scope.Dispose();
        _provider.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void SinglePane_RequestFromLeft_OpensFolderInNewRightPane()
    {
        _tab.LeftPane.NavigateTo(_parent);
        var leftPath = _tab.LeftPane.CurrentPath;

        _tab.OnOpenInOtherPaneRequested(_tab.LeftPane, _child);

        _tab.IsDualPane.Must().BeTrue();
        _tab.RightPane.Must().NotBeNull();
        _tab.ActivePane.Must().Be(_tab.RightPane);
        PathUtilities.PathsEqual(_tab.LeftPane.CurrentPath, leftPath).Must().BeTrue();
        PathUtilities.PathsEqual(_tab.RightPane!.CurrentPath, _child).Must().BeTrue();
    }

    [Fact]
    public void DualPane_RequestFromLeft_NavigatesRightAndLeavesLeftPut()
    {
        _tab.LeftPane.NavigateTo(_parent);
        _tab.ToggleDualPaneCommand.Execute(null);
        var leftPath = _tab.LeftPane.CurrentPath;

        _tab.OnOpenInOtherPaneRequested(_tab.LeftPane, _child);

        _tab.ActivePane.Must().Be(_tab.RightPane);
        PathUtilities.PathsEqual(_tab.LeftPane.CurrentPath, leftPath).Must().BeTrue();
        PathUtilities.PathsEqual(_tab.RightPane!.CurrentPath, _child).Must().BeTrue();
    }

    [Fact]
    public void DualPane_RequestFromRight_NavigatesLeftAndLeavesRightPut()
    {
        _tab.LeftPane.NavigateTo(_parent);
        _tab.ToggleDualPaneCommand.Execute(null);
        _tab.RightPane!.NavigateTo(_parent);
        var rightPath = _tab.RightPane.CurrentPath;

        _tab.OnOpenInOtherPaneRequested(_tab.RightPane, _child);

        _tab.ActivePane.Must().Be(_tab.LeftPane);
        PathUtilities.PathsEqual(_tab.RightPane.CurrentPath, rightPath).Must().BeTrue();
        PathUtilities.PathsEqual(_tab.LeftPane.CurrentPath, _child).Must().BeTrue();
    }

    [Fact]
    public void NullSender_FallsBackToActivePane()
    {
        _tab.LeftPane.NavigateTo(_parent);
        _tab.ToggleDualPaneCommand.Execute(null);
        _tab.SetActivePane(_tab.LeftPane);

        _tab.OnOpenInOtherPaneRequested(null, _child);

        _tab.ActivePane.Must().Be(_tab.RightPane);
        PathUtilities.PathsEqual(_tab.RightPane!.CurrentPath, _child).Must().BeTrue();
    }

    [Fact]
    public void ToggleDualPaneAlone_MirrorsLeftPathAndActivatesRight()
    {
        _tab.LeftPane.NavigateTo(_parent);

        _tab.ToggleDualPaneCommand.Execute(null);

        _tab.IsDualPane.Must().BeTrue();
        _tab.ActivePane.Must().Be(_tab.RightPane);
        PathUtilities.PathsEqual(_tab.RightPane!.CurrentPath, _tab.LeftPane.CurrentPath).Must().BeTrue();
    }
}
