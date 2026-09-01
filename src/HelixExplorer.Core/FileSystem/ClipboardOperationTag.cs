namespace HelixExplorer.Core.FileSystem;

/// <summary>
/// Resolves whether an OS clipboard payload still matches the last Helix Cut/Copy publish.
/// External Explorer payloads must not inherit a stale Cut tag (QUAL-2).
/// </summary>
public static class ClipboardOperationTag
{
    public static ClipboardOperation Resolve(
        IReadOnlyList<string> currentPaths,
        IReadOnlyList<string>? publishedPaths,
        ClipboardOperation? publishedOperation)
    {
        if (publishedOperation is null || publishedPaths is null || publishedPaths.Count == 0)
            return ClipboardOperation.Copy;

        if (currentPaths.Count != publishedPaths.Count)
            return ClipboardOperation.Copy;

        for (var i = 0; i < currentPaths.Count; i++)
        {
            if (!string.Equals(currentPaths[i], publishedPaths[i], StringComparison.OrdinalIgnoreCase))
                return ClipboardOperation.Copy;
        }

        return publishedOperation.Value;
    }
}
