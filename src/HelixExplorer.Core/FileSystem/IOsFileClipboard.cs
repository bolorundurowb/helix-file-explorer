namespace HelixExplorer.Core.FileSystem;

/// <summary>
/// Bridge to the OS clipboard for file/folder paths (Explorer interop).
/// Implementations live in the UI project because Avalonia storage APIs are required.
/// </summary>
public interface IOsFileClipboard
{
    Task SetFilesAsync(IReadOnlyList<string> paths, ClipboardOperation operation, CancellationToken ct = default);

    /// <summary>
    /// Returns file paths currently on the OS clipboard, if any.
    /// <see cref="ClipboardOperation"/> is only known for payloads Helix itself placed;
    /// external apps typically yield <see cref="ClipboardOperation.Copy"/>.
    /// </summary>
    Task<(IReadOnlyList<string> Paths, ClipboardOperation Operation)?> TryGetFilesAsync(CancellationToken ct = default);
}
