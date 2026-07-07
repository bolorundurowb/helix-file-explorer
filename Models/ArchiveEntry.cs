namespace HelixExplorer.Models;

/// <summary>
/// Virtual entry describing a file or folder inside an archive.
/// Path semantics use forward slashes regardless of host OS.
/// </summary>
public sealed class ArchiveEntry
{
    public string FullPath { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public DateTime Modified { get; init; }
    public string Extension { get; init; } = string.Empty;

    public FileSystemEntry ToFileSystemEntry() => new(FullPath, Name, IsDirectory, Size, Modified, Extension);
}