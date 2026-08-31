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
            // Fixed 260 UTF-16LE characters (520 bytes), NUL-padded. A short read here means the
            // file is truncated/corrupt - do not hand back a path built from a partial field.
            var pathBytes = reader.ReadBytes(520);
            if (pathBytes.Length < 520)
                return null;

            originalPath = ExtractPath(pathBytes);
        }
        else
        {
            var pathLength = reader.ReadInt32();
            if (pathLength <= 0 || pathLength > 32 * 1024)
                return null;

            var pathBytes = reader.ReadBytes(pathLength * 2);
            if (pathBytes.Length < pathLength * 2)
                return null;

            originalPath = ExtractPath(pathBytes);
        }

        if (string.IsNullOrWhiteSpace(originalPath))
            return null;

        return (fileSize, deletedAt, originalPath);
    }

    /// <summary>
    /// Truncates at the first NUL rather than trimming trailing NULs: a Windows path can never
    /// legitimately contain an embedded NUL, so anything after one is either zero-padding beyond
    /// the real path or leftover disk slack from the field's previous contents - either way it is
    /// not part of the original path and must not be appended to it.
    /// </summary>
    private static string ExtractPath(byte[] pathBytes)
    {
        var raw = Encoding.Unicode.GetString(pathBytes);
        var nulIndex = raw.IndexOf('\0');
        return nulIndex >= 0 ? raw[..nulIndex] : raw;
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
