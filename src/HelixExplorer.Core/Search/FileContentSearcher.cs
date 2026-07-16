using System.Text;
using HelixExplorer.Core.Filtering;

namespace HelixExplorer.Core.Search;

public static class FileContentSearcher
{
    public static async Task<bool> ContainsAsync(
        string path,
        string query,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || GlobMatcher.HasGlobMetacharacters(query))
            return false;

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 8192,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (stream.Length == 0)
            return false;

        if (stream.Length > maxBytes)
            return false;

        var length = (int)Math.Min(stream.Length, maxBytes);
        var buffer = new byte[length];
        var read = await stream.ReadAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
        if (read <= 0)
            return false;

        if (TextFileClassifier.LooksBinary(buffer.AsSpan(0, read)))
            return false;

        var text = Encoding.UTF8.GetString(buffer, 0, read);
        return text.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
