namespace HelixExplorer.Core.Models;

/// <summary>Immutable snapshot of a directory's contents handed across the UI boundary.</summary>
public sealed class DirectoryListing(string path, IReadOnlyList<FileSystemEntry> entries)
{
    public static DirectoryListing Empty { get; } = new(string.Empty, Array.Empty<FileSystemEntry>());

    public string Path { get; } = path;
    public IReadOnlyList<FileSystemEntry> Entries { get; } = entries;
    public int Count => Entries.Count;
}
