using System.Buffers;
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

        cancellationToken.ThrowIfCancellationRequested();
        if (maxBytes <= 0)
            return false;

        byte[]? buffer = null;
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 8192,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            if (stream.Length == 0)
                return false;

            // Large files still get a useful bounded prefix scan instead of being skipped entirely.
            var length = checked((int)Math.Min(Math.Min(stream.Length, maxBytes), Array.MaxLength));
            buffer = ArrayPool<byte>.Shared.Rent(length);
            var read = 0;
            while (read < length)
            {
                var current = await stream.ReadAsync(buffer.AsMemory(read, length - read), cancellationToken)
                    .ConfigureAwait(false);
                if (current == 0)
                    break;
                read += current;
            }

            if (read == 0)
                return false;

            var bytes = buffer.AsSpan(0, read);
            var (encoding, bomLength) = DetectEncoding(bytes);
            if (encoding is null && TextFileClassifier.LooksBinary(bytes))
                return false;

            encoding ??= new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
            var text = encoding.GetString(bytes[bomLength..]);
            return text.Contains(query, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            // A locked or concurrently removed file must not abort a multi-file search.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            if (buffer is not null)
                ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static (Encoding? Encoding, int BomLength) DetectEncoding(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(Encoding.UTF8.Preamble))
            return (Encoding.UTF8, Encoding.UTF8.Preamble.Length);
        if (bytes.StartsWith(Encoding.Unicode.Preamble))
            return (Encoding.Unicode, Encoding.Unicode.Preamble.Length);
        if (bytes.StartsWith(Encoding.BigEndianUnicode.Preamble))
            return (Encoding.BigEndianUnicode, Encoding.BigEndianUnicode.Preamble.Length);
        return (null, 0);
    }
}
