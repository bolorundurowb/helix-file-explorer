using Avalonia.Threading;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Persistence;
using HelixExplorer.Core.Settings;
using HelixExplorer.Localization;
using HelixExplorer.Services;
using HelixExplorer.ViewModels.Pane;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.ViewModels.Tests;

/// <summary>
/// A typed path that is not a folder stays on the current location. Navigating anyway would publish an
/// empty listing and look like a real empty folder. Relative text is resolved against that location.
/// </summary>
public sealed class PaneViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly PaneViewModel _pane;

    public PaneViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "helix-editable-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var dbPath = Path.Combine(_root, "helix.db");
        var settingsPath = Path.Combine(_root, "settings.json");

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddHelixApplicationServices();
        // A real listing, archive read, or watcher callback continues on the thread pool and can
        // bind Dispatcher.UIThread there once this headless job has ended.
        services.AddSingleton<IFileSystemProvider, ImmediateFileSystem>();
        services.AddSingleton<IArchiveProvider, ImmediateArchive>();
        services.AddSingleton<IGitProvider, IdleGit>();
        services.AddSingleton<IFileVisualProvider, EmptyVisuals>();
        services.AddTransient<IFileChangeWatcher, VirtualFileChangeWatcher>();
        services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore(settingsPath));
        services.AddSingleton<IAppDatabase>(sp =>
        {
            var db = new SqliteAppDatabase(sp.GetRequiredService<ISettingsStore>(), dbPath, settingsPath);
            db.Initialize();
            return db;
        });

        _provider = services.BuildServiceProvider(validateScopes: true);
        _scope = _provider.CreateScope();
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
    public void CommitEditablePath_shouldStayAndReportNotFound_whenTheAbsolutePathIsMissing()
    {
        var missing = Path.Combine(_root, "missing");

        OnUi(() =>
        {
            _pane.EditablePath = missing;
            _pane.CommitEditablePath();

            _pane.CurrentPath.Must().BeEmpty();
            _pane.IsEditingPath.Must().BeFalse();
            _pane.IsUnavailableState.Must().BeTrue();
            _pane.StatusText.Must().Be(UiStrings.FolderNotFound);
            _pane.ContentDetail.Must().Be(UiStrings.FolderNotFound);
        });
    }

    [Fact]
    public void CommitEditablePath_shouldStayAndReportNotADirectory_whenTheAbsolutePathIsAFile()
    {
        var file = Path.Combine(_root, "notes.txt");
        File.WriteAllText(file, "notes");

        OnUi(() =>
        {
            _pane.EditablePath = file;
            _pane.CommitEditablePath();

            _pane.CurrentPath.Must().BeEmpty();
            _pane.IsEditingPath.Must().BeFalse();
            _pane.IsUnavailableState.Must().BeTrue();
            _pane.StatusText.Must().Be(UiStrings.PathIsNotADirectory);
            _pane.ContentDetail.Must().Be(UiStrings.PathIsNotADirectory);
        });
    }

    [Fact]
    public void CommitEditablePath_shouldOpenTheFolder_whenTheAbsolutePathExists()
    {
        var folder = Path.Combine(_root, "dest");
        Directory.CreateDirectory(folder);

        OnNavigatedUi(() =>
        {
            _pane.EditablePath = folder;
            _pane.CommitEditablePath();

            _pane.CurrentPath.Must().Be(WithSeparator(folder));
            _pane.IsEditingPath.Must().BeFalse();
            _pane.IsHome.Must().BeFalse();
        });
    }

    [Fact]
    public void CommitEditablePath_shouldStayAndReportNotFound_whenARelativePathIsTypedFromHome()
    {
        OnUi(() =>
        {
            _pane.NavigateToHome();
            _pane.EditablePath = "docs";
            _pane.CommitEditablePath();

            _pane.IsHome.Must().BeTrue();
            _pane.CurrentPath.Must().Be(PaneConstants.HomeRoute);
            _pane.IsEditingPath.Must().BeFalse();
            _pane.StatusText.Must().Be(UiStrings.FolderNotFound);
        });
    }

    [Fact]
    public void CommitEditablePath_shouldStayAndReportNotFound_whenARelativePathIsTypedWithNoCurrentFolder()
    {
        OnUi(() =>
        {
            _pane.EditablePath = "docs";
            _pane.CommitEditablePath();

            _pane.CurrentPath.Must().BeEmpty();
            _pane.IsEditingPath.Must().BeFalse();
            _pane.IsUnavailableState.Must().BeTrue();
            _pane.StatusText.Must().Be(UiStrings.FolderNotFound);
        });
    }

    [Fact]
    public void CommitEditablePath_shouldOpenTheChild_whenTheRelativePathExists()
    {
        var parent = Path.Combine(_root, "parent");
        var child = Path.Combine(parent, "child");
        Directory.CreateDirectory(child);

        OnNavigatedUi(() =>
        {
            _pane.NavigateTo(parent);
            _pane.EditablePath = "child";
            _pane.CommitEditablePath();

            _pane.CurrentPath.Must().Be(WithSeparator(child));
            _pane.IsEditingPath.Must().BeFalse();
        });
    }

    [Fact]
    public void CommitEditablePath_shouldOpenTheSibling_whenTheRelativePathStepsUp()
    {
        var parent = Path.Combine(_root, "work");
        var current = Path.Combine(parent, "docs");
        var sibling = Path.Combine(parent, "sibling");
        Directory.CreateDirectory(current);
        Directory.CreateDirectory(sibling);

        OnNavigatedUi(() =>
        {
            _pane.NavigateTo(current);
            _pane.EditablePath = @"..\sibling";
            _pane.CommitEditablePath();

            _pane.CurrentPath.Must().Be(WithSeparator(sibling));
        });
    }

    [Fact]
    public void CommitEditablePath_shouldStayAndReportNotADirectory_whenTheRelativePathIsAFile()
    {
        var parent = Path.Combine(_root, "parent");
        Directory.CreateDirectory(parent);
        File.WriteAllText(Path.Combine(parent, "readme.txt"), "readme");

        OnNavigatedUi(() =>
        {
            _pane.NavigateTo(parent);
            var stay = _pane.CurrentPath;
            _pane.EditablePath = "readme.txt";
            _pane.CommitEditablePath();

            _pane.CurrentPath.Must().Be(stay);
            _pane.IsUnavailableState.Must().BeTrue();
            _pane.StatusText.Must().Be(UiStrings.PathIsNotADirectory);
        });
    }

    [Fact]
    public void CommitEditablePath_shouldStayAndReportNotFound_whenTheRelativePathIsMissing()
    {
        var parent = Path.Combine(_root, "parent");
        Directory.CreateDirectory(parent);

        OnNavigatedUi(() =>
        {
            _pane.NavigateTo(parent);
            var stay = _pane.CurrentPath;
            _pane.EditablePath = "missing";
            _pane.CommitEditablePath();

            _pane.CurrentPath.Must().Be(stay);
            _pane.IsUnavailableState.Must().BeTrue();
            _pane.StatusText.Must().Be(UiStrings.FolderNotFound);
        });
    }

    [Fact]
    public void CommitEditablePath_shouldOpenAnArchive_whenThePathIsAZip()
    {
        var zip = Path.Combine(_root, "notes.zip");
        File.WriteAllBytes(zip, []);

        OnNavigatedUi(() =>
        {
            _pane.EditablePath = zip;
            _pane.CommitEditablePath();

            _pane.CurrentPath.Must().Be(ArchivePath.Mount(Path.GetFullPath(zip)));
            _pane.IsArchive.Must().BeTrue();
        });
    }

    private void OnUi(Action body) => HeadlessSession.RunOnUiThread(body);

    // Navigation starts a refresh on the thread pool. The headless dispatcher only pumps that hop
    // while this job is running, so the refresh has to finish before the job returns.
    private void OnNavigatedUi(Action body)
        => HeadlessSession.RunOnUiThread(async () =>
        {
            try
            {
                body();
            }
            finally
            {
                await _pane.RefreshAsync().ConfigureAwait(true);
                // Git status is posted after the refresh await returns, so drain it before the job ends.
                Dispatcher.UIThread.RunJobs();
                _pane.Dispose();
            }

            return 0;
        });

    private static string WithSeparator(string path)
        => PaneNavigationController.EnsureTrailingSeparator(Path.GetFullPath(path));

    private sealed class EmptyVisuals : IFileVisualProvider
    {
        public ValueTask<FileVisualData?> GetAsync(FileVisualRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<FileVisualData?>(null);
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
}
