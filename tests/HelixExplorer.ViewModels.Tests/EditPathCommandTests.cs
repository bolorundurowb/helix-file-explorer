using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Persistence;
using HelixExplorer.Core.Settings;
using HelixExplorer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.ViewModels.Tests;

/// <summary>
/// Go to Folder is a window command, so it has to work from Home as well as from a folder, and stay
/// disabled when there is no pane to edit.
/// </summary>
public sealed class EditPathCommandTests : IDisposable
{
    private readonly string _root;

    public EditPathCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "helix-edit-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void EditPath_shouldBeginEditing_whenTheActivePaneIsHome()
    {
        WithViewModel(vm =>
        {
            vm.InitializeWindow(restoreSession: false);

            vm.ActivePane!.IsHome.Must().BeTrue();
            vm.EditPathCommand.CanExecute(null).Must().BeTrue();

            vm.EditPathCommand.Execute(null);

            vm.ActivePane.IsEditingPath.Must().BeTrue();
            vm.ActivePane.EditablePath.Must().Be(PaneConstants.HomeRoute);
        });
    }

    [Fact]
    public void EditPath_shouldBeginEditing_whenTheActivePaneShowsAFolder()
    {
        var folder = Path.Combine(_root, "docs");
        Directory.CreateDirectory(folder);

        WithViewModel(async vm =>
        {
            vm.InitializeWindow(restoreSession: false, initialPath: folder);

            vm.EditPathCommand.CanExecute(null).Must().BeTrue();
            vm.EditPathCommand.Execute(null);

            vm.ActivePane!.IsEditingPath.Must().BeTrue();
            vm.ActivePane.EditablePath.Must().Be(vm.ActivePane.CurrentPath);
            await vm.ActivePane.RefreshAsync();
        });
    }

    [Fact]
    public void EditPath_shouldBeUnavailable_whenThereIsNoPane()
    {
        WithViewModel(vm =>
        {
            vm.EditPathCommand.CanExecute(null).Must().BeFalse();

            var command = CommandRegistry.Palette.Single(c => c.Id == "navigation.goToFolder");
            command.CanExecute!(vm).Must().BeFalse();
        });
    }

    private void WithViewModel(Action<MainWindowViewModel> body)
        => WithViewModel(vm =>
        {
            body(vm);
            return Task.CompletedTask;
        });

    private void WithViewModel(Func<MainWindowViewModel, Task> body)
    {
        var settingsPath = Path.Combine(_root, "settings.json");
        new JsonSettingsStore(settingsPath).Save(new AppSettings
        {
            RestoreSessionOnStartup = false,
            AutoCheckForUpdates = false,
        });

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddHelixApplicationServices();
        services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore(settingsPath));
        services.AddSingleton<IAppDatabase>(sp =>
        {
            var db = new SqliteAppDatabase(sp.GetRequiredService<ISettingsStore>(), Path.Combine(_root, "helix.db"), settingsPath);
            db.Initialize();
            return db;
        });
        // Real network discovery finishes after this dispatcher job and then binds the UI thread to the wrong thread.
        services.AddSingleton<INetworkLocationProvider, EmptyNetworkLocations>();
        services.AddSingleton<INetworkDiscoveryAvailability, AvailableNetworkDiscovery>();

        HeadlessSession.RunOnUiThread(async () =>
        {
            using var provider = services.BuildServiceProvider(validateScopes: true);
            using var scope = provider.CreateScope();
            var vm = scope.ServiceProvider.GetRequiredService<MainWindowViewModel>();
            await body(vm).ConfigureAwait(true);
            return 0;
        });
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
