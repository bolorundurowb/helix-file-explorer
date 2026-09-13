using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Persistence;
using HelixExplorer.Core.Settings;
using HelixExplorer.Services;
using HelixExplorer.ViewModels.Pane;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.ViewModels.Tests;

public class TabViewModelTransferToOtherPaneTests : IDisposable
{
    private readonly string _root;
    private readonly string _leftDir;
    private readonly string _rightDir;
    private readonly string _dbPath;
    private readonly string _settingsPath;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly TabViewModel _tab;

    public TabViewModelTransferToOtherPaneTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "helix-transfer-pane-" + Guid.NewGuid().ToString("N"));
        _leftDir = Path.Combine(_root, "left");
        _rightDir = Path.Combine(_root, "right");
        Directory.CreateDirectory(_leftDir);
        Directory.CreateDirectory(_rightDir);

        _dbPath = Path.Combine(_root, "helix.db");
        _settingsPath = Path.Combine(_root, "settings.json");

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddHelixApplicationServices();
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
    public void SinglePane_CommandsDisabled()
    {
        _tab.LeftPane.NavigateTo(_leftDir);

        _tab.IsDualPane.Must().BeFalse();
        _tab.CanCopyToOtherPane.Must().BeFalse();
        _tab.CanMoveToOtherPane.Must().BeFalse();
        _tab.LeftPane.CopyToOtherPaneCommand.CanExecute(null).Must().BeFalse();
        _tab.LeftPane.MoveToOtherPaneCommand.CanExecute(null).Must().BeFalse();
    }

    [Fact]
    public void DualPane_NoSelection_CommandsDisabled()
    {
        _tab.LeftPane.NavigateTo(_leftDir);
        _tab.ToggleDualPaneCommand.Execute(null);
        _tab.RightPane!.NavigateTo(_rightDir);

        _tab.IsDualPane.Must().BeTrue();
        _tab.CanCopyToOtherPane.Must().BeFalse();
        _tab.LeftPane.CopyToOtherPaneCommand.CanExecute(null).Must().BeFalse();
        _tab.LeftPane.MoveToOtherPaneCommand.CanExecute(null).Must().BeFalse();
    }

    [Fact]
    public void DualPane_WithSelection_CommandsEnabled()
    {
        var testFile = Path.Combine(_leftDir, "sample.txt");
        File.WriteAllText(testFile, "hello");

        _tab.LeftPane.NavigateTo(_leftDir);
        _tab.ToggleDualPaneCommand.Execute(null);
        _tab.RightPane!.NavigateTo(_rightDir);

        var entry = new FileSystemEntry(testFile, Path.GetFileName(testFile), false, 5, DateTime.UtcNow, ".txt");
        _tab.LeftPane.UpdateSelection([new EntryItemViewModel(entry)]);
        _tab.SetActivePane(_tab.LeftPane);

        _tab.LeftPane.CanTransferToOtherPane().Must().BeTrue();
        _tab.LeftPane.CopyToOtherPaneCommand.CanExecute(null).Must().BeTrue();
        _tab.LeftPane.MoveToOtherPaneCommand.CanExecute(null).Must().BeTrue();
        _tab.CanCopyToOtherPane.Must().BeTrue();
        _tab.CanMoveToOtherPane.Must().BeTrue();
    }

    [Fact]
    public void DualPane_RightPaneActive_WithSelection_CommandsEnabled()
    {
        var testFile = Path.Combine(_rightDir, "sample.txt");
        File.WriteAllText(testFile, "hello");

        _tab.LeftPane.NavigateTo(_leftDir);
        _tab.ToggleDualPaneCommand.Execute(null);
        _tab.RightPane!.NavigateTo(_rightDir);

        var entry = new FileSystemEntry(testFile, Path.GetFileName(testFile), false, 5, DateTime.UtcNow, ".txt");
        _tab.RightPane.UpdateSelection([new EntryItemViewModel(entry)]);
        _tab.SetActivePane(_tab.RightPane);

        _tab.RightPane.CanTransferToOtherPane().Must().BeTrue();
        _tab.RightPane.CopyToOtherPaneCommand.CanExecute(null).Must().BeTrue();
        _tab.RightPane.MoveToOtherPaneCommand.CanExecute(null).Must().BeTrue();
        _tab.CanCopyToOtherPane.Must().BeTrue();
        _tab.CanMoveToOtherPane.Must().BeTrue();
    }

    [Fact]
    public void DualPane_ClosingDualPane_DisablesCommands()
    {
        var testFile = Path.Combine(_leftDir, "sample.txt");
        File.WriteAllText(testFile, "hello");

        _tab.LeftPane.NavigateTo(_leftDir);
        _tab.ToggleDualPaneCommand.Execute(null);
        _tab.RightPane!.NavigateTo(_rightDir);

        var entry = new FileSystemEntry(testFile, Path.GetFileName(testFile), false, 5, DateTime.UtcNow, ".txt");
        _tab.LeftPane.UpdateSelection([new EntryItemViewModel(entry)]);
        _tab.SetActivePane(_tab.LeftPane);

        _tab.LeftPane.CanTransferToOtherPane().Must().BeTrue();

        _tab.ToggleDualPaneCommand.Execute(null);

        _tab.IsDualPane.Must().BeFalse();
        _tab.LeftPane.CanTransferToOtherPane().Must().BeFalse();
        _tab.LeftPane.CopyToOtherPaneCommand.CanExecute(null).Must().BeFalse();
        _tab.LeftPane.MoveToOtherPaneCommand.CanExecute(null).Must().BeFalse();
        _tab.CanCopyToOtherPane.Must().BeFalse();
    }

    [Fact]
    public void PaneCommands_RaiseTransferEvents()
    {
        var pane = _scope.ServiceProvider.GetRequiredService<IPaneViewModelFactory>().Create();
        pane.IsDualPane = true;

        var testFile = Path.Combine(_leftDir, "sample.txt");
        File.WriteAllText(testFile, "hello");
        pane.NavigateTo(_leftDir);

        var entry = new FileSystemEntry(testFile, Path.GetFileName(testFile), false, 5, DateTime.UtcNow, ".txt");
        pane.UpdateSelection([new EntryItemViewModel(entry)]);

        IReadOnlyList<string>? copiedPaths = null;
        IReadOnlyList<string>? movedPaths = null;

        pane.CopyToOtherPaneRequested += (_, paths) => copiedPaths = paths;
        pane.MoveToOtherPaneRequested += (_, paths) => movedPaths = paths;

        pane.CopyToOtherPaneCommand.Execute(null);
        copiedPaths.Must().NotBeNull();
        copiedPaths!.Count.Must().Be(1);
        copiedPaths[0].Must().Be(testFile);

        pane.MoveToOtherPaneCommand.Execute(null);
        movedPaths.Must().NotBeNull();
        movedPaths!.Count.Must().Be(1);
        movedPaths[0].Must().Be(testFile);

        pane.Dispose();
    }

    [Fact]
    public void CommandPalette_RegistersCopyAndMoveToOtherPane()
    {
        var palette = new CommandPaletteService();
        var target = new System.Collections.ObjectModel.ObservableCollection<CommandItem>();
        palette.FilterInto(target, "Other Pane", []);

        target.Any(c => c.Title == "Copy to Other Pane" && c.Shortcut == "Alt+F5").Must().BeTrue();
        target.Any(c => c.Title == "Move to Other Pane" && c.Shortcut == "Alt+F6").Must().BeTrue();
    }
}
