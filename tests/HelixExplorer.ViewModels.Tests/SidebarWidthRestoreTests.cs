using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Persistence;
using HelixExplorer.Core.Settings;
using HelixExplorer.Services;
using HelixExplorer.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.ViewModels.Tests;

/// <summary>
/// The sidebar column must take the saved width. A failed Width binding is only a warning in Avalonia,
/// so without this the column silently stays star-sized and fills to its MaxWidth on any wide window.
/// </summary>
public sealed class SidebarWidthRestoreTests : IDisposable
{
    private readonly string _root;

    public SidebarWidthRestoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "helix-sidebar-width-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void MainWindow_AppliesSavedSidebarWidth_InsteadOfStarSizingToMax()
    {
        var settingsPath = Path.Combine(_root, "settings.json");
        new JsonSettingsStore(settingsPath).Save(new AppSettings { SidebarWidth = 300 });

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddHelixApplicationServices();
        // Keep the real composition root but point persistence at a temp profile.
        services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore(settingsPath));
        services.AddSingleton<IAppDatabase>(sp =>
        {
            var db = new SqliteAppDatabase(sp.GetRequiredService<ISettingsStore>(), Path.Combine(_root, "helix.db"), settingsPath);
            db.Initialize();
            return db;
        });
        // Real network discovery finishes on a thread-pool thread after this test's dispatcher job has ended;
        // its continuation would then bind the reset Dispatcher.UIThread to the wrong thread and break later tests.
        services.AddSingleton<INetworkLocationProvider, EmptyNetworkLocations>();
        services.AddSingleton<INetworkDiscoveryAvailability, AvailableNetworkDiscovery>();

        // The VM and its panes own Avalonia objects, so build, show, and dispose all on the headless UI thread.
        var (columnWidth, sidebarWidth) = HeadlessSession.RunOnUiThread(() =>
        {
            using var provider = services.BuildServiceProvider(validateScopes: true);
            using var scope = provider.CreateScope();

            var vm = scope.ServiceProvider.GetRequiredService<MainWindowViewModel>();
            var window = scope.ServiceProvider.GetRequiredService<MainWindow>();
            window.DataContext = vm;
            window.Width = 1600;
            window.Height = 900;
            try
            {
                window.Show();
                window.Measure(new Size(1600, 900));
                window.Arrange(new Rect(0, 0, 1600, 900));

                var sidebar = window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "SidebarBorder");
                var column = ((Grid)sidebar.Parent!).ColumnDefinitions[0];
                return (column.Width, sidebar.Bounds.Width);
            }
            finally
            {
                window.Close();
            }
        });

        columnWidth.IsAbsolute.Must().BeTrue();
        columnWidth.Value.Must().Be(300);
        sidebarWidth.Must().Be(300);
    }

    private sealed class EmptyNetworkLocations : INetworkLocationProvider
    {
        public ValueTask<NetworkDiscoveryResult> GetNetworkLocationsAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(NetworkDiscoveryResult.Empty);
    }

    private sealed class AvailableNetworkDiscovery : INetworkDiscoveryAvailability
    {
        public bool IsUnavailable => false;
        public event EventHandler? AvailabilityChanged;
        public void Refresh() => AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
