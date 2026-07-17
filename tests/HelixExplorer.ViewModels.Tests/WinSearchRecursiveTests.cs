using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Windows.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;

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

        result.Entries.Must().Contain(e => e.Name == "ReadMe.TXT");
        result.Entries.Must().NotContain(e => e.Name == "other.dat");
    }

    [Fact]
    public async Task Search_CapsResultsAndReportsCapped()
    {
        for (var i = 0; i < 20; i++)
            File.WriteAllText(Path.Combine(_root, $"match-{i}.txt"), "x");

        var result = await _provider.SearchRecursiveAsync(_root, "match", new SearchOptions { MaxResults = 5 });

        result.Capped.Must().BeTrue();
        result.Entries.Count.Must().Be(5);
    }

    [Fact]
    public async Task Search_RespectsMaxDepth()
    {
        var deep = Path.Combine(_root, "a", "b", "c");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "target.txt"), "x");

        var shallow = await _provider.SearchRecursiveAsync(_root, "target", new SearchOptions { MaxDepth = 1 });
        shallow.Entries.Must().NotContain(e => e.Name == "target.txt");

        var deepEnough = await _provider.SearchRecursiveAsync(_root, "target", new SearchOptions { MaxDepth = 5 });
        deepEnough.Entries.Must().Contain(e => e.Name == "target.txt");
    }

    [Fact]
    public async Task Search_Cancellation_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Ensure.ThrowsAsync<OperationCanceledException>(
            async () => await _provider.SearchRecursiveAsync(_root, "x", SearchOptions.Default, cts.Token));
    }

    [Fact]
    public async Task Search_FindsTextFileByContent()
    {
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "unique-token-helix-42");
        File.WriteAllText(Path.Combine(_root, "other.txt"), "nothing here");

        var result = await _provider.SearchRecursiveAsync(_root, "unique-token-helix-42", SearchOptions.Default);

        result.Entries.Must().Contain(e => e.Name == "notes.txt");
        result.Entries.Must().NotContain(e => e.Name == "other.txt");
    }

    [Fact]
    public async Task Search_GlobQuery_DoesNotScanContent()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "*.cs appears in content");
        File.WriteAllText(Path.Combine(_root, "code.cs"), "class X {}");

        var result = await _provider.SearchRecursiveAsync(_root, "*.cs", SearchOptions.Default);

        result.Entries.Must().Contain(e => e.Name == "code.cs");
        result.Entries.Must().NotContain(e => e.Name == "a.txt");
    }

    [Fact]
    public async Task Search_SupportsNameGlobs()
    {
        File.WriteAllText(Path.Combine(_root, "alpha.pdf"), "x");
        File.WriteAllText(Path.Combine(_root, "beta.txt"), "x");

        var result = await _provider.SearchRecursiveAsync(_root, "*.pdf", SearchOptions.Default);

        result.Entries.Must().Contain(e => e.Name == "alpha.pdf");
        result.Entries.Must().NotContain(e => e.Name == "beta.txt");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private sealed class StubShellEnumerator : IShellFolderEnumerator
    {
        public ValueTask<IReadOnlyList<FileSystemEntry>> EnumerateAsync(string shellPath, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<FileSystemEntry>>(Array.Empty<FileSystemEntry>());

        public ValueTask RestoreAsync(string itemPath, string? destinationPath = null, CancellationToken ct = default) => ValueTask.CompletedTask;

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
