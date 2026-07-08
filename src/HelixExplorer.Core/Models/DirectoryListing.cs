namespace HelixExplorer.Core.Models;

/// <summary>Immutable snapshot of a directory's contents handed across the UI boundary.</summary>
public sealed class DirectoryListing
{
    public static DirectoryListing Empty { get; } = new(string.Empty, Array.Empty<FileSystemEntry>());

    public DirectoryListing(string path, IReadOnlyList<FileSystemEntry> entries)
    {
        Path = path;
        Entries = entries;
    }

    public string Path { get; }
    public IReadOnlyList<FileSystemEntry> Entries { get; }
    public int Count => Entries.Count;
}
