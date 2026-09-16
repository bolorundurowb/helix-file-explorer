using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;

namespace HelixExplorer.ViewModels.Tests;

public sealed class VirtualFileSystemTests
{
    [Fact]
    public async Task DeepTree_IsListableAndSearchable()
    {
        var fileSystem = new VirtualFileSystem();
        var provider = new VirtualFileSystemProvider(fileSystem);

        const int depth = 50;
        var current = string.Empty;
        for (var i = 0; i < depth; i++)
        {
            current = current.Length == 0 ? $"level-{i}" : Path.Combine(current, $"level-{i}");
            fileSystem.AddDirectory(current);
        }

        var leaf = Path.Combine(current, "leaf.txt");
        fileSystem.AddFile(leaf, "deep-token");

        var listing = await provider.GetDirectoryContentsAsync(fileSystem.Root);
        listing.Entries.Must().Contain(e => e.Name == "level-0" && e.IsDirectory);

        var search = await provider.SearchRecursiveAsync(fileSystem.Root, "leaf", SearchOptions.Default with { MaxDepth = depth + 1 });
        search.Entries.Must().Contain(e => e.Name == "leaf.txt");
    }

    [Fact]
    public async Task DeniedDirectory_ThrowsOnListing()
    {
        var fileSystem = new VirtualFileSystem();
        var provider = new VirtualFileSystemProvider(fileSystem);
        fileSystem.AddDirectory("secret");
        fileSystem.SetAccessDenied("secret");

        await Ensure.ThrowsAsync<UnauthorizedAccessException>(
            async () => await provider.GetDirectoryContentsAsync(Path.Combine(fileSystem.Root, "secret")));
    }

    [Fact]
    public async Task DeniedDirectory_IsSkippedDuringSearch()
    {
        var fileSystem = new VirtualFileSystem();
        var provider = new VirtualFileSystemProvider(fileSystem);
        fileSystem.AddDirectory("secret");
        fileSystem.AddFile("secret/hidden.txt", "target-token");
        fileSystem.AddFile("visible.txt", "target-token");
        fileSystem.SetAccessDenied("secret");

        var result = await provider.SearchRecursiveAsync(fileSystem.Root, "target", SearchOptions.Default);

        result.Entries.Must().Contain(e => e.Name == "visible.txt");
        result.Entries.Must().NotContain(e => e.Name == "hidden.txt");
    }

    [Fact]
    public void Delete_RemovesDirectoryAndDescendants()
    {
        var fileSystem = new VirtualFileSystem();
        var provider = new VirtualFileSystemProvider(fileSystem);
        fileSystem.AddDirectory("folder");
        fileSystem.AddFile("folder/nested.txt", "x");

        fileSystem.Delete("folder");

        provider.DirectoryExists(Path.Combine(fileSystem.Root, "folder")).Must().BeFalse();
        provider.FileExists(Path.Combine(fileSystem.Root, "folder", "nested.txt")).Must().BeFalse();
    }

    [Fact]
    public void FileExists_And_DirectoryExists_TrackFileKind()
    {
        var fileSystem = new VirtualFileSystem();
        var provider = new VirtualFileSystemProvider(fileSystem);
        fileSystem.AddFile("doc.txt", "x");

        provider.FileExists(Path.Combine(fileSystem.Root, "doc.txt")).Must().BeTrue();
        provider.DirectoryExists(Path.Combine(fileSystem.Root, "doc.txt")).Must().BeFalse();
    }

    [Fact]
    public void Watcher_Notify_RaisesChanged()
    {
        using var watcher = new VirtualFileChangeWatcher();
        var raised = 0;
        watcher.Changed += (_, _) => raised++;
        watcher.Watch(@"C:\anywhere");

        watcher.Notify();

        raised.Must().Be(1);
        watcher.IsWatching.Must().BeTrue();
    }
}
