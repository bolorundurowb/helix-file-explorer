using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.ViewModels.Tests;

/// <summary>
/// Shared contract suite for <see cref="IFileSystemProvider"/>. Concrete subclasses supply an
/// <see cref="IFileSystemHarness"/> (virtual or real) so the same behavioral guarantees are verified
/// against both the in-memory harness and the real Windows provider.
/// </summary>
public abstract class IFileSystemProviderContractTests : IDisposable
{
    private readonly IFileSystemHarness _harness;

    protected IFileSystemProviderContractTests(IFileSystemHarness harness)
    {
        _harness = harness;
        Seed(harness);
    }

    protected IFileSystemProvider Provider => _harness.Provider;

    protected string Root => _harness.Root;

    private static void Seed(IFileSystemHarness harness)
    {
        harness.AddFile("alpha.txt", "hello world");
        harness.AddFile("beta.txt", "nothing here");
        harness.AddFile("notes.txt", "unique-token-helix-42");
        harness.AddFile("code.cs", "class X {}");
        harness.AddFile("glob-content.txt", "*.cs appears in content");
        harness.AddFile("hidden.dat", "hidden");
        harness.SetHidden("hidden.dat");
        harness.AddDirectory("folder");
        harness.AddFile("folder/nested.txt", "nested content");
        harness.AddDirectory("folder/sub");
        harness.AddFile("folder/sub/deep.txt", "deep content");
        for (var i = 0; i < 10; i++)
            harness.AddFile($"match-{i}.txt", "x");
    }

    [Fact]
    public async Task GetDirectoryContents_ReturnsFilesAndDirectories()
    {
        var listing = await Provider.GetDirectoryContentsAsync(Root);

        listing.Path.Must().Be(Root);
        listing.Entries.Must().Contain(e => e.Name == "alpha.txt" && !e.IsDirectory);
        listing.Entries.Must().Contain(e => e.Name == "folder" && e.IsDirectory);
        listing.Entries.Must().Contain(e => e.Name == "hidden.dat");
    }

    [Fact]
    public async Task GetDirectoryContents_SortsFoldersFirstThenName()
    {
        var entries = (await Provider.GetDirectoryContentsAsync(Root)).Entries;

        for (var i = 1; i < entries.Count; i++)
        {
            var previous = entries[i - 1];
            var current = entries[i];

            if (previous.IsDirectory != current.IsDirectory)
            {
                previous.IsDirectory.Must().BeTrue();
                current.IsDirectory.Must().BeFalse();
                continue;
            }

            (StringComparer.OrdinalIgnoreCase.Compare(previous.Name, current.Name) <= 0).Must().BeTrue();
        }
    }

    [Fact]
    public async Task GetDirectoryContents_MissingDirectory_ReturnsEmpty()
    {
        var listing = await Provider.GetDirectoryContentsAsync(Path.Combine(Root, "missing"));

        listing.Count.Must().Be(0);
    }

    [Fact]
    public async Task GetDirectoryContents_Subdirectory_ReturnsChildren()
    {
        var listing = await Provider.GetDirectoryContentsAsync(Path.Combine(Root, "folder"));

        listing.Entries.Must().Contain(e => e.Name == "nested.txt");
        listing.Entries.Must().Contain(e => e.Name == "sub" && e.IsDirectory);
    }

    [Fact]
    public void DirectoryAndFileExists_ReflectsTree()
    {
        Provider.DirectoryExists(Root).Must().BeTrue();
        Provider.DirectoryExists(Path.Combine(Root, "folder")).Must().BeTrue();
        Provider.FileExists(Path.Combine(Root, "alpha.txt")).Must().BeTrue();
        Provider.FileExists(Path.Combine(Root, "folder")).Must().BeFalse();
        Provider.DirectoryExists(Path.Combine(Root, "alpha.txt")).Must().BeFalse();
        Provider.DirectoryExists(Path.Combine(Root, "missing")).Must().BeFalse();
        Provider.FileExists(Path.Combine(Root, "missing.txt")).Must().BeFalse();
    }

    [Fact]
    public void ResolvePath_IsIdempotentForAbsolutePath()
    {
        Provider.ResolvePath(Root).Must().Be(Root);
    }

    [Fact]
    public async Task Search_IsCaseInsensitive()
    {
        var result = await Provider.SearchRecursiveAsync(Root, "ALPHA", SearchOptions.Default);

        result.Entries.Must().Contain(e => e.Name == "alpha.txt");
        result.Entries.Must().NotContain(e => e.Name == "beta.txt");
    }

    [Fact]
    public async Task Search_CapsResultsAndReportsCapped()
    {
        var result = await Provider.SearchRecursiveAsync(Root, "match", new SearchOptions { MaxResults = 5 });

        result.Capped.Must().BeTrue();
        result.Entries.Count.Must().Be(5);
    }

    [Fact]
    public async Task Search_RespectsMaxDepth()
    {
        var shallow = await Provider.SearchRecursiveAsync(Root, "deep", new SearchOptions { MaxDepth = 1 });
        shallow.Entries.Must().NotContain(e => e.Name == "deep.txt");

        var deepEnough = await Provider.SearchRecursiveAsync(Root, "deep", new SearchOptions { MaxDepth = 5 });
        deepEnough.Entries.Must().Contain(e => e.Name == "deep.txt");
    }

    [Fact]
    public async Task Search_Cancellation_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Ensure.ThrowsAsync<OperationCanceledException>(
            async () => await Provider.SearchRecursiveAsync(Root, "x", SearchOptions.Default, cts.Token));
    }

    [Fact]
    public async Task Search_FindsTextFileByContent()
    {
        var result = await Provider.SearchRecursiveAsync(Root, "unique-token-helix-42", SearchOptions.Default);

        result.Entries.Must().Contain(e => e.Name == "notes.txt");
    }

    [Fact]
    public async Task Search_GlobQuery_DoesNotScanContent()
    {
        var result = await Provider.SearchRecursiveAsync(Root, "*.cs", SearchOptions.Default);

        result.Entries.Must().Contain(e => e.Name == "code.cs");
        result.Entries.Must().NotContain(e => e.Name == "glob-content.txt");
    }

    [Fact]
    public async Task Search_HiddenFilesExcludedUnlessRequested()
    {
        var byDefault = await Provider.SearchRecursiveAsync(Root, "hidden", SearchOptions.Default);
        byDefault.Entries.Must().NotContain(e => e.Name == "hidden.dat");

        var withHidden = await Provider.SearchRecursiveAsync(Root, "hidden", new SearchOptions { IncludeHiddenAndSystem = true });
        withHidden.Entries.Must().Contain(e => e.Name == "hidden.dat");
    }

    public void Dispose() => _harness.Dispose();
}
