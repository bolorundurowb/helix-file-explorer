using Avalonia.Threading;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Git;
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
        // Listing, clipboard, and volume watches continue after the dispatcher job and bind
        // Dispatcher.UIThread to a pool thread for the rest of the suite.
        services.AddSingleton<IFileSystemProvider, ImmediateFileSystem>();
        services.AddSingleton<IArchiveProvider, ImmediateArchive>();
        services.AddSingleton<IGitProvider, IdleGit>();
        services.AddSingleton<IFileVisualProvider, EmptyVisuals>();
        services.AddSingleton<IOsFileClipboard, EmptyClipboard>();
        services.AddSingleton<IVolumeChangeWatcher, QuietVolumeWatcher>();
        services.AddTransient<IFileChangeWatcher, VirtualFileChangeWatcher>();
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
            Dispatcher.UIThread.RunJobs();
            return 0;
        });
    }

    private sealed class EmptyVisuals : IFileVisualProvider
    {
        public ValueTask<FileVisualData?> GetAsync(FileVisualRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<FileVisualData?>(null);
    }

    private sealed class EmptyClipboard : IOsFileClipboard
    {
        public Task SetFilesAsync(IReadOnlyList<string> paths, ClipboardOperation operation, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<(IReadOnlyList<string> Paths, ClipboardOperation Operation)?> TryGetFilesAsync(CancellationToken ct = default)
            => Task.FromResult<(IReadOnlyList<string> Paths, ClipboardOperation Operation)?>(null);
    }

    private sealed class QuietVolumeWatcher : IVolumeChangeWatcher
    {
        public event EventHandler? VolumesChanged { add { } remove { } }

        public void Start() { }

        public void Dispose() { }
    }

    private sealed class ImmediateFileSystem : IFileSystemProvider
    {
        public ValueTask<DirectoryListing> GetDirectoryContentsAsync(string path, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new DirectoryListing(path, Array.Empty<FileSystemEntry>()));

        public ValueTask<SearchResult> SearchRecursiveAsync(
            string path,
            string query,
            SearchOptions options,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(SearchResult.Empty);

        public string ResolvePath(string path) => Path.GetFullPath(path);

        public bool DirectoryExists(string path)
        {
            try { return Directory.Exists(path); }
            catch (IOException) { return false; }
        }

        public bool FileExists(string path)
        {
            try { return File.Exists(path); }
            catch (IOException) { return false; }
        }
    }

    private sealed class ImmediateArchive : IArchiveProvider
    {
        public bool IsArchiveFile(string path) => ArchivePath.IsArchiveFile(path);

        public ValueTask<IReadOnlyList<FileSystemEntry>> EnumerateAsync(string virtualPath, CancellationToken token = default)
            => ValueTask.FromResult<IReadOnlyList<FileSystemEntry>>(Array.Empty<FileSystemEntry>());

        public ValueTask<string?> ExtractEntryAsync(string virtualPath, CancellationToken token = default)
            => ValueTask.FromResult<string?>(null);

        public ValueTask CreateZipAsync(IReadOnlyList<string> sourcePaths, string destinationZipPath, CancellationToken token = default)
            => ValueTask.CompletedTask;

        public ValueTask ExtractArchiveToDirectoryAsync(string archivePath, string destinationDirectory, CancellationToken token = default)
            => ValueTask.CompletedTask;

        public ValueTask ExtractVirtualEntriesAsync(IReadOnlyList<string> virtualPaths, string destinationDirectory, CancellationToken token = default)
            => ValueTask.CompletedTask;

        public void CleanupExtractedFiles() { }
    }

    private sealed class IdleGit : IGitProvider
    {
        public bool IsInsideRepository(string path) => false;

        public ValueTask<GitStatusSnapshot> GetStatusAsync(string path, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(GitStatusSnapshot.Empty);

        public ValueTask<IReadOnlyList<string>> ListBranchesAsync(string path, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public ValueTask<bool> CheckoutBranchAsync(string path, string branch, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
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
