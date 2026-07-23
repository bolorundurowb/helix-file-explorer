using HelixExplorer.Core.Archives;
using Microsoft.Extensions.Logging.Abstractions;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers.Zip;

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
