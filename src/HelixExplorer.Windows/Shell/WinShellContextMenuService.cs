using System.Diagnostics;
using System.Runtime.InteropServices;
using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Windows.Shell;

/// <summary>
/// Shows the Explorer-compatible shell context menu via IShellFolder / IContextMenu.
/// Falls back gracefully when COM fails so Helix app menus still work alone.
/// </summary>
public sealed class WinShellContextMenuService : IShellContextMenuService
{
    private const uint IdCmdFirst = 100;
    private const uint IdCmdLast = 0x7FFF;

    public ValueTask ShowMoreOptionsAsync(
        string folderPath,
        IReadOnlyList<string> paths,
        nint ownerHwnd,
        int screenX,
        int screenY,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(folderPath) && paths.Count > 0)
            folderPath = Path.GetDirectoryName(paths[0]) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(folderPath))
            return ValueTask.CompletedTask;

        try
        {
            ShowContextMenu((IntPtr)ownerHwnd, folderPath, paths, screenX, screenY);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WinShellContextMenuService.ShowMoreOptionsAsync failed: {ex}");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ShowPropertiesAsync(string path, nint ownerHwnd, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path))
            return ValueTask.CompletedTask;

        try
        {
            var info = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                fMask = Shell32Native.SEE_MASK_INVOKEIDLIST,
                hwnd = (IntPtr)ownerHwnd,
                lpVerb = "properties",
                lpFile = path,
                nShow = Shell32Native.SW_SHOWNORMAL
            };

            if (!Shell32Native.ShellExecuteEx(ref info))
            {
                var error = Marshal.GetLastWin32Error();
                Debug.WriteLine($"ShellExecuteEx(properties) failed: {error}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WinShellContextMenuService.ShowPropertiesAsync failed: {ex}");
        }

        return ValueTask.CompletedTask;
    }

    private static void ShowContextMenu(
        IntPtr hwnd,
        string folderPath,
        IReadOnlyList<string> selectedPaths,
        int screenX,
        int screenY)
    {
        if (Shell32Native.SHGetDesktopFolder(out var desktop) != 0)
            return;

        uint attr = 0;
        var hr = desktop.ParseDisplayName(IntPtr.Zero, IntPtr.Zero, folderPath, 0, out var pidlFull, ref attr);
        if (hr != 0 || pidlFull == IntPtr.Zero)
        {
            Debug.WriteLine($"ParseDisplayName failed for '{folderPath}': 0x{hr:X8}");
            return;
        }

        var folderPtr = IntPtr.Zero;
        var cmPtr = IntPtr.Zero;
        IntPtr[]? childPidls = null;
        var cidl = 0;

        try
        {
            var iidShellFolder = ShellIID.IID_IShellFolder;
            var hrBind = desktop.BindToObject(pidlFull, IntPtr.Zero, ref iidShellFolder, out folderPtr);
            if (hrBind != 0 || folderPtr == IntPtr.Zero)
                return;

            var folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPtr);

            if (selectedPaths.Count > 0)
            {
                childPidls = new IntPtr[selectedPaths.Count];
                foreach (var path in selectedPaths)
                {
                    if (string.IsNullOrEmpty(path))
                        continue;

                    var fileName = Path.GetFileName(path.TrimEnd('\\', '/'));
                    if (string.IsNullOrEmpty(fileName))
                        continue;

                    uint fileAttr = 0;
                    var hrFile = folder.ParseDisplayName(
                        IntPtr.Zero,
                        IntPtr.Zero,
                        fileName,
                        0,
                        out var childPidl,
                        ref fileAttr);
                    if (hrFile == 0 && childPidl != IntPtr.Zero)
                        childPidls[cidl++] = childPidl;
                }
            }

            var apidl = cidl > 0 && childPidls is not null ? childPidls[..cidl] : null;
            var iidCm = ShellIID.IID_IContextMenu;
            uint reserved = 0;
            var hrCm = folder.GetUIObjectOf(IntPtr.Zero, (uint)cidl, apidl, ref iidCm, ref reserved, out cmPtr);
            if (hrCm != 0 || cmPtr == IntPtr.Zero)
            {
                Debug.WriteLine($"GetUIObjectOf failed: 0x{hrCm:X8}");
                return;
            }

            var cm = (IContextMenu)Marshal.GetObjectForIUnknown(cmPtr);
            TrackAndInvoke(hwnd, cm, screenX, screenY);
        }
        finally
        {
            if (cmPtr != IntPtr.Zero)
                Marshal.Release(cmPtr);
            if (folderPtr != IntPtr.Zero)
                Marshal.Release(folderPtr);
            FreePidls(childPidls, cidl);
            Shell32Native.SHFree(pidlFull);
        }
    }

    private static void TrackAndInvoke(IntPtr hwnd, IContextMenu cm, int screenX, int screenY)
    {
        var hmenu = Shell32Native.CreatePopupMenu();
        if (hmenu == IntPtr.Zero)
            return;

        try
        {
            var hrQ = cm.QueryContextMenu(hmenu, 0, IdCmdFirst, IdCmdLast, Shell32Native.CMF_NORMAL);
            if (hrQ < 0)
            {
                Debug.WriteLine($"QueryContextMenu returned 0x{hrQ:X8}");
                return;
            }

            if (screenX == 0 && screenY == 0)
            {
                // Fallback near cursor if no coordinates were supplied.
                GetCursorPos(out var pt);
                screenX = pt.X;
                screenY = pt.Y;
            }

            var cmdId = Shell32Native.TrackPopupMenuEx(
                hmenu,
                Shell32Native.TPM_LEFTALIGN | Shell32Native.TPM_TOPALIGN | Shell32Native.TPM_RETURNCMD,
                screenX,
                screenY,
                hwnd == IntPtr.Zero ? GetDesktopWindow() : hwnd,
                IntPtr.Zero);

            if (cmdId == 0)
                return;

            var offset = (uint)(cmdId - IdCmdFirst);
            var ici = new CMINVOKECOMMANDINFO
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                fMask = 0,
                hwnd = hwnd,
                lpVerb = (IntPtr)offset,
                lpParameters = IntPtr.Zero,
                lpDirectory = IntPtr.Zero,
                nShow = Shell32Native.SW_SHOWNORMAL
            };

            var hrInv = cm.InvokeCommand(ref ici);
            if (hrInv < 0)
                Debug.WriteLine($"InvokeCommand returned 0x{hrInv:X8}");
        }
        finally
        {
            Shell32Native.DestroyMenu(hmenu);
        }
    }

    private static void FreePidls(IntPtr[]? pidls, int count)
    {
        if (pidls is null)
            return;

        for (var i = 0; i < count; i++)
        {
            if (pidls[i] != IntPtr.Zero)
            {
                Shell32Native.SHFree(pidls[i]);
                pidls[i] = IntPtr.Zero;
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
