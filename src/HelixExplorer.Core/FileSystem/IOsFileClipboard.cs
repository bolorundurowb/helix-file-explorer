namespace HelixExplorer.Core.FileSystem;

/// <summary>
/// Implementations live in the UI project because Avalonia storage APIs are required.
/// </summary>
public interface IOsFileClipboard
{
    Task SetFilesAsync(IReadOnlyList<string> paths, ClipboardOperation operation, CancellationToken ct = default);

    /// <summary>
    /// <see cref="ClipboardOperation"/> is only known for payloads Helix itself placed;
    /// external apps typically yield <see cref="ClipboardOperation.Copy"/>.
    /// </summary>
    Task<(IReadOnlyList<string> Paths, ClipboardOperation Operation)?> TryGetFilesAsync(CancellationToken ct = default);
}
