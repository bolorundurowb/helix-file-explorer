using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Persistence;
using HelixExplorer.Core.Settings;
using HelixExplorer.Services;
using HelixExplorer.ViewModels.Pane;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.ViewModels.Tests;

public class TabCloseRangeCommandTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly TabViewModel _tab;

    public TabCloseRangeCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "helix-close-range-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddHelixApplicationServices();
        // Keep the real composition root but point persistence at a temp profile.
        services.AddSingleton<IAppDatabase>(_ =>
        {
            var db = new SqliteAppDatabase(
                new JsonSettingsStore(Path.Combine(_root, "settings.json")),
                Path.Combine(_root, "helix.db"),
                Path.Combine(_root, "settings.json"));
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
    public void CloseTabsToRightCommand_RaisesRequest()
    {
        var raised = false;
        _tab.CloseTabsToRightRequested += (_, _) => raised = true;
        _tab.CloseTabsToRightCommand.Execute(null);
        raised.Must().BeTrue();
    }

    [Fact]
    public void CloseTabsToLeftCommand_RaisesRequest()
    {
        var raised = false;
        _tab.CloseTabsToLeftRequested += (_, _) => raised = true;
        _tab.CloseTabsToLeftCommand.Execute(null);
        raised.Must().BeTrue();
    }

    [Fact]
    public void CloseOtherTabsCommand_RaisesRequest()
    {
        var raised = false;
        _tab.CloseOtherTabsRequested += (_, _) => raised = true;
        _tab.CloseOtherTabsCommand.Execute(null);
        raised.Must().BeTrue();
    }
}
