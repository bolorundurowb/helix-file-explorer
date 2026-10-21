using Avalonia.Controls;
using Avalonia.Input;
using HelixExplorer.Core.Persistence;
using HelixExplorer.Core.Settings;
using HelixExplorer.Services;
using HelixExplorer.ViewModels.Pane;
using HelixExplorer.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.ViewModels.Tests;

/// <summary>
/// Avalonia raises <see cref="ContextMenu.Opening"/> before it records where the menu opened and never sets
/// <see cref="ContextMenu.PlacementTarget"/> for a right-click, so the pane view must resolve its menus to its own
/// pane rather than to the placement target.
/// </summary>
public sealed class PaneViewContextMenuTests : IDisposable
{
    private const string SortByHeader = "Sort by";

    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly PaneViewModel _pane;
    private readonly PaneViewModel _otherPane;

    public PaneViewContextMenuTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "helix-pane-context-menu-" + Guid.NewGuid().ToString("N"));
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
        var factory = _scope.ServiceProvider.GetRequiredService<IPaneViewModelFactory>();
        // Panes touch Dispatcher.UIThread, so they are created on the headless thread.
        (_pane, _otherPane) = HeadlessSession.RunOnUiThread(() => (factory.Create(), factory.Create()));
    }

    public void Dispose()
    {
        HeadlessSession.RunOnUiThread(() =>
        {
            _pane.Dispose();
            _otherPane.Dispose();
        });
        _scope.Dispose();
        _provider.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    [Theory]
    [InlineData("EntryContextMenu")]
    [InlineData("EmptyAreaContextMenu")]
    public void BindingAPane_SeedsSortBySubmenu_BeforeTheMenuFirstOpens(string menuKey)
    {
        var count = HeadlessSession.RunOnUiThread(() =>
        {
            var view = new PaneView { DataContext = _pane };
            var menu = (ContextMenu)view.FindResource(menuKey)!;
            return SortBySubmenu(menu).Items.Count;
        });

        // Options plus one separator between each group.
        var expected = CommandRegistry.SortMenu.Sum(g => g.Count) + CommandRegistry.SortMenu.Count - 1;
        count.Must().Be(expected);
    }

    [Fact]
    public void RightClick_ResolvesMenuToOwningPane_AndActivatesIt()
    {
        var (activations, boundToPane, sortByFilled) = HeadlessSession.RunOnUiThread(() =>
        {
            var view = new PaneView { DataContext = _pane };
            var window = new Window { Content = view, Width = 800, Height = 600 };
            window.Show();

            var activations = 0;
            void OnActivationRequested(object? sender, EventArgs e) => activations++;
            _pane.ActivationRequested += OnActivationRequested;
            try
            {
                var menu = RequestEmptyAreaMenu(view);
                var result = (activations, ReferenceEquals(menu.DataContext, _pane), SortBySubmenu(menu).Items.Count > 0);
                menu.Close();
                return result;
            }
            finally
            {
                _pane.ActivationRequested -= OnActivationRequested;
                window.Close();
            }
        });

        activations.Must().Be(1);
        boundToPane.Must().BeTrue();
        sortByFilled.Must().BeTrue();
    }

    [Fact]
    public void RightClick_AfterViewIsRebound_TargetsTheNewPane()
    {
        // PaneContentHost reuses one PaneView across tabs and swaps, so a menu bound on an earlier open must not
        // keep pointing at the previous pane.
        var (previousActivations, newActivations, boundToNewPane) = HeadlessSession.RunOnUiThread(() =>
        {
            var view = new PaneView { DataContext = _pane };
            var window = new Window { Content = view, Width = 800, Height = 600 };
            window.Show();

            var previous = 0;
            var current = 0;
            void OnPrevious(object? sender, EventArgs e) => previous++;
            void OnCurrent(object? sender, EventArgs e) => current++;
            try
            {
                RequestEmptyAreaMenu(view).Close();

                view.DataContext = _otherPane;
                _pane.ActivationRequested += OnPrevious;
                _otherPane.ActivationRequested += OnCurrent;

                var menu = RequestEmptyAreaMenu(view);
                var result = (previous, current, ReferenceEquals(menu.DataContext, _otherPane));
                menu.Close();
                return result;
            }
            finally
            {
                _pane.ActivationRequested -= OnPrevious;
                _otherPane.ActivationRequested -= OnCurrent;
                window.Close();
            }
        });

        previousActivations.Must().Be(0);
        newActivations.Must().Be(1);
        boundToNewPane.Must().BeTrue();
    }

    private static ContextMenu RequestEmptyAreaMenu(PaneView view)
    {
        // The view's root surface carries EmptyAreaContextMenu; raising ContextRequested goes through Avalonia's
        // own open path, which is what raises Opening with a null PlacementTarget.
        var surface = (Control)view.Content!;
        surface.RaiseEvent(new ContextRequestedEventArgs());
        return surface.ContextMenu!;
    }

    private static MenuItem SortBySubmenu(ContextMenu menu)
        => menu.Items.OfType<MenuItem>().Single(item => item.Header as string == SortByHeader);
}
