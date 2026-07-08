using Avalonia;

namespace HelixExplorer.Services;

/// <summary>
/// Native Win11-style context menus via direct Shell32 / IShellFolder P/Invoke. No
/// SharpShell dependency. Resolves the PIDL of the parent folder, binds its
/// <c>IShellFolder</c>, and asks for <c>IContextMenu</c> for the supplied file list
/// (or for an empty selection, the folder background).
/// </summary>
public interface IContextMenuService
{
    /// <summary>
    /// Shows the native context menu at <paramref name="screenPoint"/> for the
    /// files in <paramref name="selectedFiles"/> located in <paramref name="folderPath"/>.
    /// If <paramref name="selectedFiles"/> is null/empty, the folder's own menu is shown.
    /// </summary>
    void ShowContextMenu(IntPtr hwnd, string folderPath, string[]? selectedFiles, PixelPoint screenPoint);
}