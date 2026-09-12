namespace HelixExplorer.Core.Models;

/// <summary>
/// Keep this a readonly record struct so listings can accumulate in pooled buffers cheaply.
/// </summary>
public readonly record struct FileSystemEntry(
    string FullPath,
    string Name,
    bool IsDirectory,
    long SizeBytes,
    DateTime ModifiedUtc,
    string Extension,
    bool IsHidden = false,
    string? OriginalPath = null,
    DateTime? DeletedAtUtc = null)
{
    public string TypeLabel => IsDirectory
        ? "Folder"
        : string.IsNullOrEmpty(Extension)
            ? "File"
            : Extension.TrimStart('.').ToUpperInvariant() + " File";
}
