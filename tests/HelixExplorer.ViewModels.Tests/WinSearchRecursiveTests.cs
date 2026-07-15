using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Windows.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HelixExplorer.ViewModels.Tests;

public class WinSearchRecursiveTests : IDisposable
{
    private readonly string _root;
    private readonly WinFileSystemProvider _provider;

    public WinSearchRecursiveTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "helix-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new WinFileSystemProvider(new StubShellEnumerator(), NullLogger<WinFileSystemProvider>.Instance);
    }

    [Fact]
    public async Task Search_IsCaseInsensitive()
    {
        File.WriteAllText(Path.Combine(_root, "ReadMe.TXT"), "x");
        File.WriteAllText(Path.Combine(_root, "other.dat"), "x");

        var result = await _provider.SearchRecursiveAsync(_root, "readme", SearchOptions.Default);

        Assert.Contains(result.Entries, e => e.Name == "ReadMe.TXT");
        Assert.DoesNotContain(result.Entries, e => e.Name == "other.dat");
    }

    [Fact]
    public async Task Search_CapsResultsAndReportsCapped()
    {
        for (var i = 0; i < 20; i++)
            File.WriteAllText(Path.Combine(_root, $"match-{i}.txt"), "x");

        var result = await _provider.SearchRecursiveAsync(_root, "match", new SearchOptions { MaxResults = 5 });

        Assert.True(result.Capped);
        Assert.Equal(5, result.Entries.Count);
    }

    [Fact]
    public async Task Search_RespectsMaxDepth()
    {
        var deep = Path.Combine(_root, "a", "b", "c");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "target.txt"), "x");

        var shallow = await _provider.SearchRecursiveAsync(_root, "target", new SearchOptions { MaxDepth = 1 });
        Assert.DoesNotContain(shallow.Entries, e => e.Name == "target.txt");

        var deepEnough = await _provider.SearchRecursiveAsync(_root, "target", new SearchOptions { MaxDepth = 5 });
        Assert.Contains(deepEnough.Entries, e => e.Name == "target.txt");
    }

    [Fact]
    public async Task Search_Cancellation_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await _provider.SearchRecursiveAsync(_root, "x", SearchOptions.Default, cts.Token));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private sealed class StubShellEnumerator : IShellFolderEnumerator
    {
        public ValueTask<IReadOnlyList<FileSystemEntry>> EnumerateAsync(string shellPath, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<FileSystemEntry>>(Array.Empty<FileSystemEntry>());

        public ValueTask RestoreAsync(string itemPath, CancellationToken ct = default) => ValueTask.CompletedTask;

        public ValueTask EmptyRecycleBinAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    }
}
