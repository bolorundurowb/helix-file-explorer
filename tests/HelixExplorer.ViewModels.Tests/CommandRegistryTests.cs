using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using HelixExplorer.Controls;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Persistence;
using HelixExplorer.Core.Settings;
using HelixExplorer.Services;
using HelixExplorer.ViewModels.Pane;
using HelixExplorer.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.ViewModels.Tests;

/// <summary>
/// The palette, the toolbar flyouts, and both pane context menus are built from one registry, so a sort or
/// group state change must show identically everywhere and the folder colour list must be the same in every menu.
/// </summary>
public sealed class CommandRegistryTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly PaneViewModel _pane;

    public CommandRegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "helix-command-registry-" + Guid.NewGuid().ToString("N"));
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
        // The pane touches Dispatcher.UIThread; creating it on the headless thread keeps a test-pool thread
        // from claiming the dispatcher before the headless session starts.
        _pane = HeadlessSession.RunOnUiThread(
            () => _scope.ServiceProvider.GetRequiredService<IPaneViewModelFactory>().Create());
    }

    public void Dispose()
    {
        HeadlessSession.RunOnUiThread(_pane.Dispose);
        _scope.Dispose();
        _provider.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void CommandIds_AreUnique()
    {
        var ids = CommandRegistry.Palette.Select(c => c.Id)
            .Concat(PaneMenus().SelectMany(m => m).SelectMany(g => g).Select(c => c.Id))
            .ToList();

        ids.Distinct(StringComparer.Ordinal).Count().Must().Be(ids.Count);
    }

    [Fact]
    public void PaneMenuOptions_AllDeclareCheckedState()
    {
        foreach (var definition in PaneMenus().SelectMany(m => m).SelectMany(g => g))
            definition.IsChecked.Must().NotBeNull();
    }

    [Fact]
    public void SortDirection_ExactlyOneOptionChecked()
    {
        var direction = CommandRegistry.SortMenu[1];

        _pane.SortDescending = true;
        direction.Single(d => d.IsChecked!(_pane)).Id.Must().Be("sort.descending");

        _pane.SortDescending = false;
        direction.Single(d => d.IsChecked!(_pane)).Id.Must().Be("sort.ascending");
    }

    [Fact]
    public void ExecutingOption_ChangesPaneAndChecksThatOption()
    {
        var groupBySize = CommandRegistry.GroupByMenu.SelectMany(g => g).Single(d => d.Id == "group.size");

        groupBySize.Execute(_pane);

        _pane.GroupBy.Must().Be(GroupByMode.Size);
        CommandRegistry.GroupByMenu.SelectMany(g => g).Single(d => d.IsChecked!(_pane)).Must().Be(groupBySize);
    }

    [Fact]
    public void FilledSortMenu_ReflectsPaneStateWithSeparatorsBetweenGroups()
    {
        _pane.SortColumn = SortColumn.Size;
        _pane.SortDescending = true;
        _pane.DirectorySort = DirectorySortMode.FoldersFirst;

        var checkedHeaders = HeadlessSession.RunOnUiThread(() =>
        {
            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = "Sort by", Tag = CommandRegistry.SortMenu });
            CommandMenuBuilder.FillTaggedSubmenus(menu.Items, _pane);

            var items = ((MenuItem)menu.Items[0]!).Items;
            items.OfType<Separator>().Count().Must().Be(CommandRegistry.SortMenu.Count - 1);
            return items.OfType<MenuItem>().Where(i => i.IsChecked).Select(i => (string)i.Header!).ToList();
        });

        checkedHeaders.Must().BeSequenceEqual(new[] { "Size", "Descending", "Sort folders first" });
    }

    [Fact]
    public void ReopeningMenu_RebuildsInsteadOfDuplicating()
    {
        var count = HeadlessSession.RunOnUiThread(() =>
        {
            var flyout = new MenuFlyout();
            CommandMenuBuilder.Fill(flyout.Items, CommandRegistry.LayoutMenu, _pane);
            CommandMenuBuilder.Fill(flyout.Items, CommandRegistry.LayoutMenu, _pane);
            return flyout.Items.Count;
        });

        count.Must().Be(CommandRegistry.LayoutMenu.Sum(g => g.Count));
    }

    [Fact]
    public void EmptyToolbarFlyout_IsFilledWhenItOpens()
    {
        // Counts what the open presenter renders, not flyout.Items: a plain MenuFlyout reports the generated
        // items on Items while its presenter stays bound to the empty sentinel and shows nothing.
        var (isOpen, rendered) = OpenFilledToolbarFlyout(new CommandMenuFlyout());

        isOpen.Must().BeTrue();
        rendered.Must().Be(CommandRegistry.GroupByMenu.Sum(g => g.Count));
    }

    [Fact]
    public void PlainEmptyMenuFlyout_FilledOnOpening_RendersNothing()
    {
        // Pins the Avalonia behaviour CommandMenuFlyout works around. If this starts failing, Avalonia has fixed
        // the sentinel binding and CommandMenuFlyout can go back to being a plain MenuFlyout.
        var (_, rendered) = OpenFilledToolbarFlyout(new MenuFlyout());

        rendered.Must().Be(0);
    }

    private (bool IsOpen, int Rendered) OpenFilledToolbarFlyout(MenuFlyout flyout)
        => HeadlessSession.RunOnUiThread(() =>
        {
            flyout.Opening += (_, _) => CommandMenuBuilder.Fill(flyout.Items, CommandRegistry.GroupByMenu, _pane);
            var button = new Button { Flyout = flyout };
            var window = new Window { Content = button, Width = 400, Height = 300 };
            window.Show();
            try
            {
                flyout.ShowAt(button);
                var presenter = (MenuFlyoutPresenter)flyout.Popup.Child!;
                return (flyout.IsOpen, presenter.Items.OfType<MenuItem>().Count());
            }
            finally
            {
                flyout.Hide();
                window.Close();
            }
        });

    [Fact]
    public void ColorSubmenu_ListsPaletteBeforeStaticEntries_AndDoesNotDuplicate()
    {
        var headers = HeadlessSession.RunOnUiThread(() =>
        {
            var submenu = new MenuItem { Header = "Folder colour", Tag = FolderColorPalette.All };
            submenu.Items.Add(new MenuItem { Header = "Clear colour" });
            var menu = new ContextMenu();
            menu.Items.Add(submenu);

            CommandMenuBuilder.FillTaggedSubmenus(menu.Items, _pane);
            CommandMenuBuilder.FillTaggedSubmenus(menu.Items, _pane);

            return submenu.Items.OfType<MenuItem>().Select(i => (string)i.Header!).ToList();
        });

        headers.Must().BeSequenceEqual(FolderColorPalette.All.Select(c => c.Name).Append("Clear colour").ToArray());
    }

    [Fact]
    public void TintSwatches_FillFourColumnGrid_WithoutTextAndWithoutDuplicates()
    {
        var (count, parameters, anyTextHeader, columns) = HeadlessSession.RunOnUiThread(() =>
        {
            var submenu = new MenuItem { Header = "Change tab color", Tag = TabTintPalette.All };
            var menu = new ContextMenu();
            menu.Items.Add(submenu);

            CommandMenuBuilder.FillTintSwatches(menu.Items, NoOpCommand.Instance);
            CommandMenuBuilder.FillTintSwatches(menu.Items, NoOpCommand.Instance);

            var items = submenu.Items.OfType<MenuItem>().ToList();
            var panel = submenu.ItemsPanel?.Build();
            return (
                items.Count,
                items.Select(i => (string)i.CommandParameter!).ToList(),
                items.Any(i => i.Header is string),
                panel is UniformGrid grid ? grid.Columns : 0);
        });

        count.Must().Be(TabTintPalette.All.Count);
        parameters.Must().BeSequenceEqual(TabTintPalette.All.Select(c => c.Hex).ToArray());
        anyTextHeader.Must().BeFalse();
        columns.Must().Be(4);
    }

    private static IEnumerable<IReadOnlyList<IReadOnlyList<CommandDefinition<PaneViewModel>>>> PaneMenus()
        => [CommandRegistry.SortMenu, CommandRegistry.GroupByMenu, CommandRegistry.LayoutMenu];

    private sealed class NoOpCommand : ICommand
    {
        public static NoOpCommand Instance { get; } = new();

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
        }
    }
}
