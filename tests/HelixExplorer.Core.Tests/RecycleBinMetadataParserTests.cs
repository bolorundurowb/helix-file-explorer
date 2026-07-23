using System.Text;
using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Core.Tests;

public class RecycleBinMetadataParserTests
{
    [Fact]
    public void TryParse_Version2_ReadsOriginalPath()
    {
        var deletedAt = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var originalPath = @"C:\Users\test\Documents\report.txt";
        using var stream = BuildVersion2(size: 1234, deletedAt, originalPath);

        var parsed = RecycleBinMetadataParser.TryParse(stream);

        parsed.Must().NotBeNull();
        parsed!.Value.Size.Must().Be(1234);
        parsed.Value.DeletedAtUtc.Must().Be(deletedAt);
        parsed.Value.OriginalPath.Must().Be(originalPath);
    }

    [Fact]
    public void TryParse_Version1_ReadsOriginalPath()
    {
        var deletedAt = new DateTime(2018, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var originalPath = @"D:\old\file.bin";
        using var stream = BuildVersion1(size: 99, deletedAt, originalPath);

        var parsed = RecycleBinMetadataParser.TryParse(stream);

        parsed.Must().NotBeNull();
        parsed!.Value.Size.Must().Be(99);
        parsed.Value.DeletedAtUtc.Must().Be(deletedAt);
        parsed.Value.OriginalPath.Must().Be(originalPath);
    }

    [Fact]
    public void TryParse_RejectsAsciiDollarIHeaderMyth()
    {
        // The old bug required bytes '$''I' at offset 0; real files start with version UInt64.
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true))
        {
            writer.Write((byte)'$');
            writer.Write((byte)'I');
            writer.Write(new byte[6]);
            writer.Write(0L);
            writer.Write(DateTime.UtcNow.ToFileTimeUtc());
            writer.Write(1);
            writer.Write(Encoding.Unicode.GetBytes("X\0"));
        }

        stream.Position = 0;
        RecycleBinMetadataParser.TryParse(stream).Must().BeNull();
    }

    private static MemoryStream BuildVersion2(long size, DateTime deletedAtUtc, string originalPath)
    {
        var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);
        writer.Write(2UL);
        writer.Write(size);
        writer.Write(deletedAtUtc.ToFileTimeUtc());
        var pathChars = originalPath + "\0";
        writer.Write(pathChars.Length);
        writer.Write(Encoding.Unicode.GetBytes(pathChars));
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream BuildVersion1(long size, DateTime deletedAtUtc, string originalPath)
    {
        var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);
        writer.Write(1UL);
        writer.Write(size);
        writer.Write(deletedAtUtc.ToFileTimeUtc());
        var pathBytes = new byte[520];
        var encoded = Encoding.Unicode.GetBytes(originalPath + "\0");
        Array.Copy(encoded, pathBytes, Math.Min(encoded.Length, pathBytes.Length));
        writer.Write(pathBytes);
        stream.Position = 0;
        return stream;
    }
}
