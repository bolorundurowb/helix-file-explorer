using HelixExplorer.Core.Search;

namespace HelixExplorer.Core.Tests;

public sealed class FileContentSearcherTests
{
    [Fact]
    public async Task ContainsAsync_QueryPresent_ReturnsTrue()
    {
        var path = CreateTempFile("The quick brown fox jumps over the lazy dog.");
        try
        {
            (await FileContentSearcher.ContainsAsync(path, "brown fox", long.MaxValue, CancellationToken.None))
                .Must().BeTrue();
        }
        finally
        {
            TryDeleteFile(path);
        }
    }

    [Fact]
    public async Task ContainsAsync_QueryIsCaseInsensitive()
    {
        var path = CreateTempFile("HelixExplorer makes file management easy.");
        try
        {
            (await FileContentSearcher.ContainsAsync(path, "helixexplorer", long.MaxValue, CancellationToken.None))
                .Must().BeTrue();
        }
        finally
        {
            TryDeleteFile(path);
        }
    }

    [Fact]
    public async Task ContainsAsync_QueryAbsent_ReturnsFalse()
    {
        var path = CreateTempFile("nothing interesting here");
        try
        {
            (await FileContentSearcher.ContainsAsync(path, "needle", long.MaxValue, CancellationToken.None))
                .Must().BeFalse();
        }
        finally
        {
            TryDeleteFile(path);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ContainsAsync_EmptyOrWhitespaceQuery_ReturnsFalse(string query)
    {
        var path = CreateTempFile("some content");
        try
        {
            (await FileContentSearcher.ContainsAsync(path, query, long.MaxValue, CancellationToken.None))
                .Must().BeFalse();
        }
        finally
        {
            TryDeleteFile(path);
        }
    }

    [Theory]
    [InlineData("*.txt")]
    [InlineData("file?.txt")]
    [InlineData("[abc]")]
    public async Task ContainsAsync_QueryWithGlobMetacharacters_ReturnsFalse(string query)
    {
        var path = CreateTempFile("content that would otherwise match " + query);
        try
        {
            (await FileContentSearcher.ContainsAsync(path, query, long.MaxValue, CancellationToken.None))
                .Must().BeFalse();
        }
        finally
        {
            TryDeleteFile(path);
        }
    }

    [Fact]
    public async Task ContainsAsync_EmptyFile_ReturnsFalse()
    {
        var path = CreateTempFile(string.Empty);
        try
        {
            (await FileContentSearcher.ContainsAsync(path, "anything", long.MaxValue, CancellationToken.None))
                .Must().BeFalse();
        }
        finally
        {
            TryDeleteFile(path);
        }
    }

    [Fact]
    public async Task ContainsAsync_FileLargerThanMaxBytes_ReturnsFalse()
    {
        var path = CreateTempFile("needle in a haystack that is longer than the allowed byte budget");
        try
        {
            (await FileContentSearcher.ContainsAsync(path, "needle", maxBytes: 4, CancellationToken.None))
                .Must().BeFalse();
        }
        finally
        {
            TryDeleteFile(path);
        }
    }

    [Fact]
    public async Task ContainsAsync_BinaryFile_ReturnsFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), "helix-content-search-" + Guid.NewGuid().ToString("N") + ".bin");
        var bytes = System.Text.Encoding.UTF8.GetBytes("needle");
        var withNul = new byte[bytes.Length + 1];
        Array.Copy(bytes, withNul, bytes.Length);
        withNul[^1] = 0;
        await File.WriteAllBytesAsync(path, withNul);
        try
        {
            (await FileContentSearcher.ContainsAsync(path, "needle", long.MaxValue, CancellationToken.None))
                .Must().BeFalse();
        }
        finally
        {
            TryDeleteFile(path);
        }
    }

    [Fact]
    public async Task ContainsAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var path = CreateTempFile("some content");
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Ensure.ThrowsAsync<OperationCanceledException>(
                async () => await FileContentSearcher.ContainsAsync(path, "content", long.MaxValue, cts.Token));
        }
        finally
        {
            TryDeleteFile(path);
        }
    }

    private static string CreateTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "helix-content-search-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, content);
        return path;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup for CI temp files.
        }
    }
}
