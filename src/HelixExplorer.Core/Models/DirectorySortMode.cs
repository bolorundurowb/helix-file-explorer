namespace HelixExplorer.Core.Models;

/// <summary>
/// Controls whether directories are grouped ahead of files when sorting a listing.
/// </summary>
public enum DirectorySortMode
{
    /// <summary>Directories always precede files, then the requested column decides order.</summary>
    FoldersFirst,

    /// <summary>Files always precede directories, then the requested column decides order.</summary>
    FilesFirst,

    /// <summary>Directories and files are interleaved and sorted purely by the requested column.</summary>
    MixedWithFiles
}
