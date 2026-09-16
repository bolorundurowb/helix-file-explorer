using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Windows.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelixExplorer.ViewModels.Tests;

/// <summary>
/// Seeds a filesystem tree (in-memory or on-disk) so the same contract tests can run against any
/// <see cref="IFileSystemProvider"/> implementation.
/// </summary>
public interface IFileSystemHarness : IDisposable
{
    string Root { get; }

    IFileSystemProvider Provider { get; }

    void AddFile(string relativePath, string content = "");

    void AddDirectory(string relativePath);

    void SetHidden(string relativePath);
}

public sealed class VirtualFileSystemHarness : IFileSystemHarness
{
    private readonly VirtualFileSystem _fileSystem;

    public VirtualFileSystemHarness()
    {
        _fileSystem = new VirtualFileSystem(@"C:\virtual-" + Guid.NewGuid().ToString("N"));
        Provider = new VirtualFileSystemProvider(_fileSystem);
    }

    public string Root => _fileSystem.Root;

    public IFileSystemProvider Provider { get; }

    public VirtualFileSystem FileSystem => _fileSystem;

    public void AddFile(string relativePath, string content = "") => _fileSystem.AddFile(relativePath, content);

    public void AddDirectory(string relativePath) => _fileSystem.AddDirectory(relativePath);

    public void SetHidden(string relativePath) => _fileSystem.SetHidden(relativePath);

    public void Dispose()
    {
    }
}

public sealed class RealFileSystemHarness : IFileSystemHarness
{
    private readonly string _root;
    private readonly WinFileSystemProvider _provider;

    public RealFileSystemHarness()
    {
        _root = Path.Combine(Path.GetTempPath(), "helix-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new WinFileSystemProvider(
            new StubShellEnumerator(),
            new NoopNetworkConnectionService(),
            NullLogger<WinFileSystemProvider>.Instance);
    }

    public string Root => _root;

    public IFileSystemProvider Provider => _provider;

    public void AddFile(string relativePath, string content = "")
    {
        var full = Path.Combine(_root, relativePath);
        var parent = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        File.WriteAllText(full, content);
    }

    public void AddDirectory(string relativePath) => Directory.CreateDirectory(Path.Combine(_root, relativePath));

    public void SetHidden(string relativePath)
    {
        var full = Path.Combine(_root, relativePath);
        File.SetAttributes(full, File.GetAttributes(full) | FileAttributes.Hidden);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private sealed class NoopNetworkConnectionService : INetworkConnectionService
    {
        public ValueTask<bool> EnsureConnectedAsync(string uncPath, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
    }

    private sealed class StubShellEnumerator : IShellFolderEnumerator
    {
        public ValueTask<IReadOnlyList<FileSystemEntry>> EnumerateAsync(string shellPath, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<FileSystemEntry>>(Array.Empty<FileSystemEntry>());

        public ValueTask<bool> RestoreAsync(string itemPath, string? destinationPath = null, CancellationToken ct = default)
            => ValueTask.FromResult(true);

        public ValueTask EmptyRecycleBinAsync(CancellationToken ct = default) => ValueTask.CompletedTask;

        public ValueTask<(long ItemCount, long TotalSize)> QueryRecycleBinAsync(CancellationToken ct = default)
            => ValueTask.FromResult((0L, 0L));

        public bool HasRecycleBinItems() => false;

#pragma warning disable CS0067
        public event EventHandler? RecycleBinChanged;
#pragma warning restore CS0067

        public void StartRecycleBinWatcher() { }

        public void StopRecycleBinWatcher() { }
    }
}
