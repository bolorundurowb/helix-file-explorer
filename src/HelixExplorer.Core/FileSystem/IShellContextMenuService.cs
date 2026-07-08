namespace HelixExplorer.Core.FileSystem;

public interface IShellContextMenuService
{
    /// <summary>
    /// Shows the native Windows shell context menu for <paramref name="paths"/>
    /// (or the folder background when empty) at the given screen coordinates.
    /// </summary>
    ValueTask ShowMoreOptionsAsync(
        string folderPath,
        IReadOnlyList<string> paths,
        nint ownerHwnd,
        int screenX,
        int screenY,
        CancellationToken cancellationToken = default);

    /// <summary>Opens the Windows properties sheet for a single path.</summary>
    ValueTask ShowPropertiesAsync(string path, nint ownerHwnd, CancellationToken cancellationToken = default);
}
