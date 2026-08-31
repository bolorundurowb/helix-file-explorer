using System.Security.Principal;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.FileSystem.Undo;

namespace HelixExplorer.Windows.Shell;

/// <summary>
/// Locating and reading the per-user recycle-bin folders backing each local volume.
/// </summary>
/// <remarks>
/// The shell exposes the Recycle Bin as one namespace folder, but on disk it is a
/// <c>$RECYCLE.BIN\{SID}</c> directory per volume holding paired <c>$I</c> (metadata) and <c>$R</c>
/// (contents) files. Reading those directly is the only way to learn where a just-deleted item landed:
/// the shell's delete callback reports the source but not the destination.
/// </remarks>
internal static class RecycleBinPaths
{
    /// <summary>
    /// The current user's recycle-bin directory on every ready local volume that has one.
    /// </summary>
    public static IReadOnlyList<string> EnumerateUserBinFolders()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrEmpty(sid))
            return [];

        var folders = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            // Network drives recycle to the server's bin, which this process cannot read, and
            // enumerating a not-ready drive spins up removable media for nothing.
            if (!drive.IsReady || drive.DriveType == DriveType.Network)
                continue;

            var path = Path.Combine(drive.RootDirectory.FullName, "$RECYCLE.BIN", sid);
            if (Directory.Exists(path))
                folders.Add(path);
        }

        return folders;
    }

    /// <summary>
    /// True when any volume's bin holds at least one item.
    /// </summary>
    public static bool HasAnyItems()
    {
        foreach (var folder in EnumerateUserBinFolders())
        {
            try
            {
                if (Directory.EnumerateFiles(folder, "$I*", SearchOption.TopDirectoryOnly).Any())
                    return true;
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }

        return false;
    }

    /// <summary>
    /// Reads every readable bin entry across all volumes.
    /// </summary>
    /// <param name="deletedOnOrAfterUtc">
    /// Skips entries older than this without parsing them. Callers matching a single delete batch pass
    /// the batch start, which keeps the scan proportional to recent deletions rather than bin size.
    /// </param>
    public static IReadOnlyList<RecycleBinEntry> ReadEntries(DateTime? deletedOnOrAfterUtc = null)
    {
        var entries = new List<RecycleBinEntry>();

        foreach (var folder in EnumerateUserBinFolders())
        {
            IEnumerable<string> iFiles;
            try
            {
                iFiles = Directory.EnumerateFiles(folder, "$I*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var iFile in iFiles)
            {
                // The write time is a cheap pre-filter; the authoritative deletion timestamp comes from
                // inside the $I file, which is checked again after parsing.
                if (deletedOnOrAfterUtc is { } cutoff)
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(iFile) < cutoff)
                            continue;
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                }

                var metadata = RecycleBinMetadataParser.TryParseFile(iFile);
                if (metadata is not { } parsed)
                    continue;

                if (deletedOnOrAfterUtc is { } after && parsed.DeletedAtUtc < after)
                    continue;

                var rFile = ToContentPath(iFile);
                if (rFile is null)
                    continue;

                entries.Add(new RecycleBinEntry(rFile, parsed.OriginalPath, parsed.DeletedAtUtc));
            }
        }

        return entries;
    }

    /// <summary>
    /// Maps a <c>$I</c> metadata path to its paired <c>$R</c> contents path, or null if unpaired.
    /// </summary>
    public static string? ToContentPath(string iFilePath)
    {
        var fileName = Path.GetFileName(iFilePath);
        if (string.IsNullOrEmpty(fileName) || !fileName.StartsWith("$I", StringComparison.Ordinal))
            return null;

        var directory = Path.GetDirectoryName(iFilePath);
        if (string.IsNullOrEmpty(directory))
            return null;

        var rPath = Path.Combine(directory, "$R" + fileName[2..]);

        // A $I without its $R is a half-removed entry; there is nothing to restore.
        return File.Exists(rPath) || Directory.Exists(rPath) ? rPath : null;
    }
}
