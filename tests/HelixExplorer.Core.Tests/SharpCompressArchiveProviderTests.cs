using HelixExplorer.Core.Archives;
using HelixExplorer.Core.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;

namespace HelixExplorer.Core.Tests;

public class SharpCompressArchiveProviderTests
{
    [Fact]
    public async Task ExtractArchiveToDirectoryAsync_ExistingFile_KeepsBoth()
    {
        var root = CreateTempDirectory();
        try
        {
            var outDir = Path.Combine(root, "out");
            Directory.CreateDirectory(outDir);
            var existing = Path.Combine(outDir, "report.txt");
            await File.WriteAllTextAsync(existing, "original");

            var archivePath = Path.Combine(root, "a.zip");
            CreateZip(archivePath, ("report.txt", "archive"));

            var provider = CreateProvider();
            await provider.ExtractArchiveToDirectoryAsync(archivePath, outDir);

            (await File.ReadAllTextAsync(existing)).Must().Be("original");
            var alternate = Path.Combine(outDir, "report (1).txt");
            File.Exists(alternate).Must().BeTrue();
            (await File.ReadAllTextAsync(alternate)).Must().Be("archive");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExtractEntryAsync_SameBasenameDifferentFolders_DoNotClobber()
    {
        var root = CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(root, "multi.zip");
            CreateZip(archivePath, ("one/readme.txt", "one"), ("two/readme.txt", "two"));

            var provider = CreateProvider();
            var first = await provider.ExtractEntryAsync(ArchivePath.Combine(archivePath, "one/readme.txt"));
            var second = await provider.ExtractEntryAsync(ArchivePath.Combine(archivePath, "two/readme.txt"));

            first.Must().NotBeNull();
            second.Must().NotBeNull();
            first!.Must().NotBe(second!);
            (await File.ReadAllTextAsync(first!)).Must().Be("one");
            (await File.ReadAllTextAsync(second!)).Must().Be("two");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("../../outside/file.txt")]
    [InlineData("sub/../../escape.txt")]
    public async Task ExtractEntryAsync_TraversalInRequestedPath_StaysInsideTempDir(string maliciousInner)
    {
        var root = CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(root, "traversal.zip");
            var entryKey = maliciousInner.Replace('\\', '/');
            CreateZip(archivePath, (entryKey, "payload"));

            var provider = CreateProvider();
            var result = await provider.ExtractEntryAsync(ArchivePath.Combine(archivePath, entryKey));

            result.Must().NotBeNull();
            var tempRoot = Path.GetFullPath(AppPaths.TempRoot);
            result!.Must().StartWith(tempRoot);
            File.Exists(result!).Must().BeTrue();
            (await File.ReadAllTextAsync(result!)).Must().Be("payload");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExtractArchiveToDirectoryAsync_TarSymlinkThenWriteThrough_NeverMaterializesSymlink()
    {
        // CVE-2026-44788-style attack: a tar symlink entry ("escape" -> outside the destination),
        // immediately followed by a file entry through that name ("escape/evil.txt"). If the
        // symlink were ever materialized on disk, the file entry's textually-safe path would
        // resolve through it and land outside the destination directory. SharpCompress only makes
        // this a *real* symlink via a caller-supplied SymbolicLinkHandler, which this provider does
        // not configure, but the provider also skips any entry that carries a LinkTarget (see
        // SharpCompressArchiveProvider), so the attack has no path to succeed even if that changes.
        var root = CreateTempDirectory();
        try
        {
            var destDir = Path.Combine(root, "out");
            var archivePath = Path.Combine(root, "escape.tar");
            File.WriteAllBytes(archivePath, BuildSymlinkEscapeTar(
                linkName: "escape",
                linkTarget: "../../outside",
                fileEntryName: "escape/evil.txt",
                fileContent: "payload"));

            var provider = CreateProvider();
            await provider.ExtractArchiveToDirectoryAsync(archivePath, destDir);

            // "escape" must be an ordinary directory the file entry's write was contained in,
            // never a real symlink pointing outside destDir.
            var escapedPath = Path.Combine(destDir, "escape");
            Directory.Exists(escapedPath).Must().BeTrue();
            new DirectoryInfo(escapedPath).LinkTarget.Must().BeNull();

            var evilPath = Path.Combine(escapedPath, "evil.txt");
            File.Exists(evilPath).Must().BeTrue();
            (await File.ReadAllTextAsync(evilPath)).Must().Be("payload");

            // Nothing should have been written outside destDir along the symlink's intended target.
            Directory.Exists(Path.Combine(root, "outside")).Must().BeFalse();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    /// <summary>
    /// Hand-builds a minimal two-entry POSIX ustar stream (a symlink entry followed by a regular
    /// file entry) using the exact header layout SharpCompress's tar reader expects. There is no
    /// public SharpCompress writer API for symlink entries, so this bypasses the writer entirely.
    /// </summary>
    private static byte[] BuildSymlinkEscapeTar(string linkName, string linkTarget, string fileEntryName, string fileContent)
    {
        using var stream = new MemoryStream();

        WriteTarHeader(stream, linkName, size: 0, typeFlag: (byte)'2', linkTarget: linkTarget);

        var contentBytes = System.Text.Encoding.UTF8.GetBytes(fileContent);
        WriteTarHeader(stream, fileEntryName, size: contentBytes.Length, typeFlag: (byte)'0', linkTarget: null);
        stream.Write(contentBytes, 0, contentBytes.Length);
        var padding = (512 - (contentBytes.Length % 512)) % 512;
        if (padding > 0)
            stream.Write(new byte[padding], 0, padding);

        // Two zero-filled 512-byte blocks mark end of archive.
        stream.Write(new byte[1024], 0, 1024);
        return stream.ToArray();
    }

    private static void WriteTarHeader(Stream output, string name, long size, byte typeFlag, string? linkTarget)
    {
        var buffer = new byte[512];
        WriteAsciiField(buffer, 0, 100, name);
        WriteOctalField(buffer, 100, 8, 0); // mode
        WriteOctalField(buffer, 108, 8, 0); // uid
        WriteOctalField(buffer, 116, 8, 0); // gid
        WriteOctalField(buffer, 124, 12, size);
        WriteOctalField(buffer, 136, 12, 0); // mtime
        buffer[156] = typeFlag;
        if (linkTarget is not null)
            WriteAsciiField(buffer, 157, 100, linkTarget);
        WriteAsciiField(buffer, 257, 6, "ustar");
        buffer[263] = (byte)'0';
        buffer[264] = (byte)'0';

        // Checksum: field itself counts as 8 spaces while summing, then gets overwritten.
        for (var i = 148; i < 156; i++)
            buffer[i] = (byte)' ';
        long sum = 0;
        foreach (var b in buffer)
            sum += b;
        WriteOctalField(buffer, 148, 8, sum);

        output.Write(buffer, 0, buffer.Length);
    }

    private static void WriteAsciiField(byte[] buffer, int offset, int length, string value)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(value);
        Array.Copy(bytes, 0, buffer, offset, Math.Min(length, bytes.Length));
    }

    private static void WriteOctalField(byte[] buffer, int offset, int length, long value)
    {
        var octal = Convert.ToString(value, 8);
        var shift = length - octal.Length - 1;
        for (var i = 0; i < shift; i++)
            buffer[offset + i] = (byte)' ';
        for (var i = 0; i < octal.Length; i++)
            buffer[offset + shift + i] = (byte)octal[i];
        // Final byte of the field is left as NUL (buffer is zero-initialized).
    }

    private static SharpCompressArchiveProvider CreateProvider()
        => new(NullLogger<SharpCompressArchiveProvider>.Instance);

    private static void CreateZip(string archivePath, params (string Key, string Content)[] entries)
    {
        using var archive = ZipArchive.CreateArchive();
        foreach (var (key, content) in entries)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            archive.AddEntry(key, new MemoryStream(bytes), closeStream: true);
        }

        archive.SaveTo(archivePath, CompressionType.Deflate);
    }

    private static string CreateTempDirectory()
        => Directory.CreateTempSubdirectory("helix-archive-tests-").FullName;

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for CI temp files.
        }
    }
}
