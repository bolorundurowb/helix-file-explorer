using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Persistence;
using HelixExplorer.Core.Settings;
using HelixExplorer.Services;
using HelixExplorer.ViewModels.Pane;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.ViewModels.Tests;

/// <summary>
/// Window-level commands (Open in Terminal shortcut, palette) act on the active pane, so a pane that asks to be
/// activated — for example when its context menu opens from the keyboard — must become the tab's active pane.
/// </summary>
public class TabViewModelPaneActivationTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly TabViewModel _tab;

    public TabViewModelPaneActivationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "helix-pane-activation-" + Guid.NewGuid().ToString("N"));
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

        var sp = _scope.ServiceProvider;
        _tab = new TabViewModel(
            sp.GetRequiredService<IClipboardService>(),
            sp.GetRequiredService<IArchiveProvider>(),
            sp.GetRequiredService<IPaneViewModelFactory>(),
            sp.GetRequiredService<HomePageViewModel>());
        _tab.LeftPane.NavigateTo(_root);
    }

    public void Dispose()
    {
        _tab.Dispose();
        _scope.Dispose();
        _provider.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void DualPane_LeftRequestsActivation_WhileRightIsActive_LeftBecomesActive()
    {
        _tab.ToggleDualPaneCommand.Execute(null);
        _tab.ActivePane.Must().Be(_tab.RightPane);

        _tab.LeftPane.RequestActivation();

        ReferenceEquals(_tab.ActivePane, _tab.LeftPane).Must().BeTrue();
        _tab.LeftPane.IsActive.Must().BeTrue();
        _tab.RightPane!.IsActive.Must().BeFalse();
    }

    [Fact]
    public void DualPane_RightRequestsActivation_WhileLeftIsActive_RightBecomesActive()
    {
        _tab.ToggleDualPaneCommand.Execute(null);
        _tab.SetActivePane(_tab.LeftPane);

        _tab.RightPane!.RequestActivation();

        ReferenceEquals(_tab.ActivePane, _tab.RightPane).Must().BeTrue();
    }

    [Fact]
    public void SinglePane_LeftRequestsActivation_StaysActive()
    {
        _tab.LeftPane.RequestActivation();

        ReferenceEquals(_tab.ActivePane, _tab.LeftPane).Must().BeTrue();
    }

    [Fact]
    public void ClosedRightPane_RequestingActivation_DoesNotTakeOverTheTab()
    {
        _tab.ToggleDualPaneCommand.Execute(null);
        var closedRight = _tab.RightPane!;
        _tab.ToggleDualPaneCommand.Execute(null);

        closedRight.RequestActivation();

        _tab.IsDualPane.Must().BeFalse();
        ReferenceEquals(_tab.ActivePane, _tab.LeftPane).Must().BeTrue();
    }
}
