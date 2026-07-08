namespace HelixExplorer.Core.Models;

/// <summary>
/// Immutable description of a file or directory entry produced by a single Win32 find pass.
/// Keep this a readonly record struct so listings can accumulate in pooled buffers cheaply.
/// </summary>
public readonly record struct FileSystemEntry(
    string FullPath,
    string Name,
    bool IsDirectory,
    long SizeBytes,
    DateTime ModifiedUtc,
    string Extension,
    bool IsHidden = false)
{
    public string TypeLabel => IsDirectory
        ? "Folder"
        : string.IsNullOrEmpty(Extension)
            ? "File"
            : Extension.TrimStart('.').ToUpperInvariant() + " File";

    public static ReadOnlyMemory<string> SplitPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return Array.Empty<string>().AsMemory();

        return path
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/'], StringSplitOptions.RemoveEmptyEntries)
            .AsMemory();
    }
}
