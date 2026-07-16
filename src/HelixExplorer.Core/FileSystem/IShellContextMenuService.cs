namespace HelixExplorer.Core.FileSystem;

public interface IShellContextMenuService
{
    ValueTask ShowMoreOptionsAsync(
        string folderPath,
        IReadOnlyList<string> paths,
        nint ownerHwnd,
        int screenX,
        int screenY,
        CancellationToken cancellationToken = default);

    ValueTask ShowPropertiesAsync(string path, nint ownerHwnd, CancellationToken cancellationToken = default);
}
