using System.Text;

namespace HelixExplorer.Core.FileSystem;

/// <summary>
/// Parses Windows Recycle Bin <c>$I*</c> metadata files (Vista+ v1 and Windows 10+ v2).
/// </summary>
public static class RecycleBinMetadataParser
{
    public static (long Size, DateTime DeletedAtUtc, string OriginalPath)? TryParse(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.Unicode, leaveOpen: true);

        if (stream.Length < 24)
            return null;

        var version = reader.ReadUInt64();
        if (version is not (1UL or 2UL))
            return null;

        var fileSize = reader.ReadInt64();
        var fileTime = reader.ReadInt64();
        DateTime deletedAt;
        try
        {
            deletedAt = DateTime.FromFileTimeUtc(fileTime);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        string originalPath;
        if (version == 1)
        {
            // Fixed 260 UTF-16LE characters (520 bytes), NUL-padded.
            var pathBytes = reader.ReadBytes(520);
            if (pathBytes.Length == 0)
                return null;

            originalPath = Encoding.Unicode.GetString(pathBytes).TrimEnd('\0');
        }
        else
        {
            var pathLength = reader.ReadInt32();
            if (pathLength <= 0 || pathLength > 32 * 1024)
                return null;

            var pathBytes = reader.ReadBytes(pathLength * 2);
            if (pathBytes.Length < pathLength * 2)
                return null;

            originalPath = Encoding.Unicode.GetString(pathBytes).TrimEnd('\0');
        }

        if (string.IsNullOrWhiteSpace(originalPath))
            return null;

        return (fileSize, deletedAt, originalPath);
    }

    public static (long Size, DateTime DeletedAtUtc, string OriginalPath)? TryParseFile(string iFilePath)
    {
        try
        {
            using var fs = new FileStream(iFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return TryParse(fs);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
