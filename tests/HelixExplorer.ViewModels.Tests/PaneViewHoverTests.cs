using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Persistence;
using HelixExplorer.Core.Settings;
using HelixExplorer.Services;
using HelixExplorer.ViewModels.Pane;
using HelixExplorer.Views;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace HelixExplorer.ViewModels.Tests;

public sealed class PaneViewHoverTests
{
    [Theory]
    [InlineData(LayoutMode.Details)]
    [InlineData(LayoutMode.List)]
    [InlineData(LayoutMode.Grid)]
    [InlineData(LayoutMode.Miller)]
    public void EntryTooltip_shouldShowMetadata_whenTheLayoutIsVisible(LayoutMode layout)
    {
        var profile = Directory.CreateTempSubdirectory("helix-hover-");
        var databasePath = Path.Combine(profile.FullName, "helix.db");
        try
        {
            var settingsPath = Path.Combine(profile.FullName, "settings.json");
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHelixApplicationServices();
            services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore(settingsPath));
            services.AddSingleton<IAppDatabase>(provider =>
            {
                var db = new SqliteAppDatabase(provider.GetRequiredService<ISettingsStore>(),
                    databasePath, settingsPath);
                db.Initialize();
                return db;
            });
            services.AddSingleton<INetworkLocationProvider, EmptyNetworkLocations>();
            services.AddSingleton<INetworkDiscoveryAvailability, AvailableNetworkDiscovery>();

            var tooltip = HeadlessSession.RunOnUiThread(() =>
            {
                using var provider = services.BuildServiceProvider(validateScopes: true);
                using var scope = provider.CreateScope();
                using var pane = scope.ServiceProvider.GetRequiredService<IPaneViewModelFactory>().Create();
                var entry = new EntryItemViewModel(new(
                    Path.Combine(profile.FullName, "report.txt"), "report.txt", false, 12, DateTime.MinValue, ".txt"));
                pane.Entries.Add(entry);
                pane.SetViewMode(layout);

                var view = new PaneView { DataContext = pane };
                var window = new Window { Content = view, Width = 900, Height = 600 };
                try
                {
                    window.Show();
                    window.Measure(new Size(900, 600));
                    window.Arrange(new Rect(0, 0, 900, 600));
                    Dispatcher.UIThread.RunJobs();

                    return view.GetVisualDescendants()
                        .OfType<Control>()
                        .Where(control => control is Grid or StackPanel &&
                                          control.IsEffectivelyVisible && ReferenceEquals(control.DataContext, entry))
                        .Select(ToolTip.GetTip)
                        .OfType<string>()
                        .Single();
                }
                finally
                {
                    window.Close();
                }
            });

            tooltip.Must().Be("report.txt\nSize: 12 B");
        }
        finally
        {
            using var poolKey = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString());
            SqliteConnection.ClearPool(poolKey);
            profile.Delete(recursive: true);
        }
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
