namespace HelixExplorer.Models;

/// <summary>
/// Immutable, zero-allocation-friendly description of a file or directory entry.
/// </summary>
public readonly record struct FileSystemEntry(
    string FullPath,
    string Name,
    bool IsDirectory,
    long Size,
    DateTime Modified,
    string Extension)
{
    /// <summary>Human readable, culture-invariant size label.</summary>
    public string DisplaySize => IsDirectory ? "--" : Size switch
    {
        < 1024 => $"{Size} B",
        < 1048576 => $"{Size / 1024.0:F1} KB",
        < 1073741824 => $"{Size / 1048576.0:F1} MB",
        < 1099511627776L => $"{Size / 1073741824.0:F2} GB",
        _ => $"{Size / 1099511627776.0:F2} TB"
    };

    /// <summary>A normalised sort key so directories always precede files.</summary>
    public string SortKey => IsDirectory ? "0_" + Name : "1_" + Name;

    /// <summary>Simple Unicode icon for the entry type.</summary>
    public string Icon => IsDirectory ? "📁" : Extension.ToLowerInvariant() switch
    {
        ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".bz2" or ".tgz" or ".txz" or ".xz" => "🗜️",
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".svg" => "🖼️",
        ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" => "🎵",
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" => "🎬",
        ".txt" or ".md" or ".log" or ".json" or ".xml" or ".cs" or ".csproj" or ".sln" => "📄",
        ".exe" or ".dll" or ".msi" => "⚙️",
        _ => "📄"
    };

    private static readonly char[] SeparatorChars =
    {
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar,
        '/'
    };

    /// <summary>Splits a virtual path (which may contain archive:// segments) into its component parts.</summary>
    public static ReadOnlyMemory<string> SplitPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return Array.Empty<string>().AsMemory();
        }

        return path.Split(SeparatorChars, StringSplitOptions.RemoveEmptyEntries).AsMemory();
    }
}