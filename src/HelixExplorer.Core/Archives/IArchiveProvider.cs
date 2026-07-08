using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.Archives;

public interface IArchiveProvider
{
    bool IsArchiveFile(string path);

    ValueTask<IReadOnlyList<FileSystemEntry>> EnumerateAsync(string virtualPath, CancellationToken token = default);

    ValueTask<string?> ExtractEntryAsync(string virtualPath, CancellationToken token = default);
}
