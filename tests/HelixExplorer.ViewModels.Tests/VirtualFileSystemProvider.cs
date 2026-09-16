using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Filtering;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Search;
using HelixExplorer.Core.Sorting;

namespace HelixExplorer.ViewModels.Tests;

/// <summary>
/// <see cref="IFileSystemProvider"/> backed by a <see cref="VirtualFileSystem"/>. Mirrors the real
/// provider's observable contract: folders-first name sorting, hidden filtering during search, result
/// capping, depth limits, and content search over in-memory file content.
/// </summary>
public sealed class VirtualFileSystemProvider(VirtualFileSystem fileSystem) : IFileSystemProvider
{
    public ValueTask<DirectoryListing> GetDirectoryContentsAsync(string path, CancellationToken cancellationToken = default)
    {
        var resolved = ResolvePath(path);
        var entry = fileSystem.Find(resolved);
        if (entry is null || !entry.IsDirectory)
            return ValueTask.FromResult(DirectoryListing.Empty);

        if (entry.AccessDenied)
            throw new UnauthorizedAccessException($"Access denied to '{resolved}'.");

        cancellationToken.ThrowIfCancellationRequested();

        var children = fileSystem.GetChildren(resolved).Select(ToEntry).ToList();
        children.Sort(FileSystemEntryComparer.For(SortColumn.Name, descending: false));

        return ValueTask.FromResult(new DirectoryListing(resolved, children));
    }

    public ValueTask<SearchResult> SearchRecursiveAsync(
        string path,
        string query,
        SearchOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(query))
            return ValueTask.FromResult(SearchResult.Empty);

        var resolved = ResolvePath(path);
        var root = fileSystem.Find(resolved);
        if (root is null || !root.IsDirectory)
            return ValueTask.FromResult(SearchResult.Empty);

        var trimmed = query.Trim();
        var scanContent = options.SearchFileContents && !GlobMatcher.HasGlobMetacharacters(trimmed);
        var results = new List<FileSystemEntry>(Math.Min(options.MaxResults, 256));
        var queue = new Queue<(VirtualFileSystem.VirtualEntry Dir, int Depth)>();
        queue.Enqueue((root, 0));
        var capped = false;

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (dir, depth) = queue.Dequeue();

            if (dir.AccessDenied)
                continue;

            foreach (var child in fileSystem.GetChildren(dir.FullPath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (child.IsHidden && !options.IncludeHiddenAndSystem)
                    continue;

                if (child.IsDirectory && depth < options.MaxDepth)
                    queue.Enqueue((child, depth + 1));

                var relative = child.FullPath.Length > root.FullPath.Length
                    ? child.FullPath[(root.FullPath.Length + 1)..].Replace('\\', '/')
                    : child.Name;

                var nameMatches = EntryNameMatcher.Matches(child.Name, trimmed)
                                  || EntryNameMatcher.Matches(relative, trimmed);

                var contentMatches = false;
                if (!nameMatches && scanContent && !child.IsDirectory
                    && TextFileClassifier.IsLikelyTextExtension(Path.GetExtension(child.Name)))
                {
                    contentMatches = ContainsContent(child, trimmed, options.MaxContentBytes);
                }

                if (!nameMatches && !contentMatches)
                    continue;

                results.Add(ToEntry(child));

                if (results.Count >= options.MaxResults)
                {
                    capped = true;
                    break;
                }
            }

            if (capped)
                break;
        }

        return ValueTask.FromResult(new SearchResult(results, capped));
    }

    public string ResolvePath(string path) => fileSystem.Resolve(path);

    public bool DirectoryExists(string path) => fileSystem.Find(ResolvePath(path))?.IsDirectory == true;

    public bool FileExists(string path) => fileSystem.Find(ResolvePath(path)) is { IsDirectory: false };

    private static bool ContainsContent(VirtualFileSystem.VirtualEntry file, string query, long maxBytes)
        => file.Content.Length > 0
           && file.Content.Length <= maxBytes
           && file.Content.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static FileSystemEntry ToEntry(VirtualFileSystem.VirtualEntry entry) => new(
        entry.FullPath,
        entry.Name,
        entry.IsDirectory,
        entry.IsDirectory ? 0 : entry.SizeBytes,
        entry.ModifiedUtc,
        entry.IsDirectory ? string.Empty : Path.GetExtension(entry.Name),
        entry.IsHidden);
}
