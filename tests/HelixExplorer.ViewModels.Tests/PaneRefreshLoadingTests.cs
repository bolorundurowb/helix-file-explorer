using Avalonia.Threading;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Search;
using HelixExplorer.ViewModels.Pane;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelixExplorer.ViewModels.Tests;

/// <summary>
/// A refresh that exits without publishing must still drop the Loading overlay when no newer refresh
/// owns it; otherwise the pane sits on "Loading…" indefinitely.
/// </summary>
public sealed class PaneRefreshLoadingTests
{
    private const string FolderPath = @"\\server\share";

    [Fact]
    public void CancelledRefresh_ClearsLoading()
    {
        var host = new FakeHost(FolderPath);
        PaneRefreshCoordinator? coordinator = null;
        var fileSystem = new FakeFileSystem((_, _) =>
        {
            coordinator!.CancelRefresh();
            throw new OperationCanceledException();
        });
        coordinator = CreateCoordinator(fileSystem);

        RunRefresh(coordinator, host).Must().BeSequenceEqual(new[] { true, false });
    }

    [Fact]
    public void PathMismatchedRefresh_ClearsLoading()
    {
        var host = new FakeHost(FolderPath);
        var fileSystem = new FakeFileSystem((path, _) =>
        {
            host.CurrentPath = @"\\server\other";
            return new DirectoryListing(path, Array.Empty<FileSystemEntry>());
        });
        var coordinator = CreateCoordinator(fileSystem);

        RunRefresh(coordinator, host).Must().BeSequenceEqual(new[] { true, false });
        host.PublishCount.Must().Be(0);
    }

    [Fact]
    public void SuccessfulRefresh_ClearsLoadingOnce()
    {
        var host = new FakeHost(FolderPath);
        var fileSystem = new FakeFileSystem((path, _) => new DirectoryListing(path, Array.Empty<FileSystemEntry>()));
        var coordinator = CreateCoordinator(fileSystem);

        RunRefresh(coordinator, host).Must().BeSequenceEqual(new[] { true, false });
        host.PublishCount.Must().Be(1);
    }

    private static List<bool> RunRefresh(PaneRefreshCoordinator coordinator, FakeHost host)
        => HeadlessSession.RunOnUiThread(async () =>
        {
            await coordinator.RefreshAsync(host, showLoading: true);
            // The fallback clear is posted from finally; a lower-priority job runs after it.
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
            return host.LoadingHistory.ToList();
        });

    private static PaneRefreshCoordinator CreateCoordinator(IFileSystemProvider fileSystem)
        => new(fileSystem, new FakeArchive(), new FakeGit(), NullLogger<PaneRefreshCoordinator>.Instance);

    private sealed class FakeHost(string path) : IPaneRefreshHost
    {
        public List<bool> LoadingHistory { get; } = [];
        public int PublishCount { get; private set; }

        public bool IsDisposed => false;
        public string CurrentPath { get; set; } = path;
        public bool IsHome => false;
        public bool IsArchive => false;
        public bool IsShellNamespace => false;
        public bool ShowHiddenFiles => true;
        public bool ShowFileExtensions => true;
        public bool IsFilterVisible => false;
        public string FilterText => string.Empty;
        public SortColumn SortColumn => SortColumn.Name;
        public bool SortDescending => false;
        public DirectorySortMode DirectorySort => DirectorySortMode.MixedWithFiles;
        public GroupByMode GroupBy => GroupByMode.None;

        public void SetLoading(bool loading) => LoadingHistory.Add(loading);
        public void SetStatusText(string text) { }

        public ListingPublishResult ApplySortAndPublish(ListingPublishRequest request)
        {
            PublishCount++;
            return new PaneListingCoordinator().ApplySortAndPublish(request);
        }

        public void OnNavigated() { }
        public void ApplyGitSnapshot(GitStatusSnapshot snapshot, GitStatus status) { }
        public void StopWatcher() { }
        public void RestartWatcher() { }
        public void RequestRefresh() { }
    }

    private sealed class FakeFileSystem(Func<string, CancellationToken, DirectoryListing> list) : IFileSystemProvider
    {
        public ValueTask<DirectoryListing> GetDirectoryContentsAsync(string path, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(list(path, cancellationToken));

        public ValueTask<SearchResult> SearchRecursiveAsync(string path, string query, SearchOptions options, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(SearchResult.Empty);

        public string ResolvePath(string path) => path;
        public bool DirectoryExists(string path) => true;
        public bool FileExists(string path) => false;
    }

    private sealed class FakeGit : IGitProvider
    {
        public bool IsInsideRepository(string path) => false;

        public ValueTask<GitStatusSnapshot> GetStatusAsync(string path, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(GitStatusSnapshot.Empty);

        public ValueTask<IReadOnlyList<string>> ListBranchesAsync(string path, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public ValueTask<bool> CheckoutBranchAsync(string path, string branch, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
    }

    private sealed class FakeArchive : IArchiveProvider
    {
        public bool IsArchiveFile(string path) => false;

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

        public void CleanupExtractedFiles()
        {
        }
    }
}
