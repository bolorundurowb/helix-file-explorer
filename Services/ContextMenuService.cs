using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using HelixExplorer.Services.Interop;

namespace HelixExplorer.Services;

/// <summary>
/// Direct-P/Invoke implementation of <see cref="IContextMenuService"/>. We use the
/// desktop IShellFolder, parse the parent folder path to a PIDL, bind its child
/// IShellFolder, then call GetUIObjectOf for IContextMenu. From there we populate a
/// context menu HMENU via QueryContextMenu, run TrackPopupMenuEx, and if the user
/// chooses an item we issue InvokeCommand with the resulting verb offset.
/// </summary>
public sealed class ContextMenuService : IContextMenuService, IDisposable
{
    private bool _disposed;
    private const uint IdCmdFirst = 100;
    private const uint IdCmdLast = 0x7FFF;

    public void ShowContextMenu(IntPtr hwnd, string folderPath, string[]? selectedFiles, PixelPoint screenPoint)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(folderPath)) return;

        try
        {
            if (Shell32.SHGetDesktopFolder(out var desktop) != 0) return;

            uint attr = 0;
            var hr = desktop.ParseDisplayName(IntPtr.Zero, IntPtr.Zero, folderPath, 0, out var pidlFull, ref attr);
            if (hr != 0 || pidlFull == IntPtr.Zero)
            {
                Debug.WriteLine($"ContextMenuService: ParseDisplayName failed for '{folderPath}': 0x{hr:X8}");
                return;
            }

            var folderPtr = IntPtr.Zero;
            var cmPtr = IntPtr.Zero;
            var cm2Ptr = IntPtr.Zero;
            var cm3Ptr = IntPtr.Zero;
            IntPtr[]? childPidls = null;
            var cidl = 0;

            try
            {
                var iidShellFolder = ShellIID.IID_IShellFolder;
                var hrB = desktop.BindToObject(pidlFull, IntPtr.Zero, ref iidShellFolder, out folderPtr);
                if (hrB != 0 || folderPtr == IntPtr.Zero) return;

                var folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPtr);

                // Build child PIDLs for selected files.
                if (selectedFiles is { Length: > 0 })
                {
                    childPidls = new IntPtr[selectedFiles.Length];
                    for (var i = 0; i < selectedFiles.Length; i++)
                    {
                        var file = selectedFiles[i];
                        if (string.IsNullOrEmpty(file)) continue;
                        uint a = 0;
                        var hrFile = folder.ParseDisplayName(IntPtr.Zero, IntPtr.Zero,
                            file, 0, out var childPidl, ref a);
                        if (hrFile == 0 && childPidl != IntPtr.Zero)
                        {
                            childPidls[cidl++] = childPidl;
                        }
                    }
                }

                var apidl = cidl > 0 && childPidls != null ? childPidls[..cidl] : null;

                var iidCm = ShellIID.IID_IContextMenu;
                uint reserved = 0;
                var hrCm = folder.GetUIObjectOf(IntPtr.Zero, (uint)cidl, apidl, ref iidCm, ref reserved, out cmPtr);
                if (hrCm != 0 || cmPtr == IntPtr.Zero)
                {
                    Debug.WriteLine($"ContextMenuService: GetUIObjectOf failed: 0x{hrCm:X8}");
                    return;
                }

                var cm = (IContextMenu)Marshal.GetObjectForIUnknown(cmPtr);

                // QI for IContextMenu3 then IContextMenu2 — required to enable owner-drawn
                // submenu items (Send To, etc.). See IContextMenu2::HandleMenuMsg.
                {
                    var iid3 = ShellIID.IID_IContextMenu3;
                    Marshal.QueryInterface(cmPtr, ref iid3, out cm3Ptr);
                    var iid2 = ShellIID.IID_IContextMenu2;
                    Marshal.QueryInterface(cmPtr, ref iid2, out cm2Ptr);
                }

                Show(hwnd, cm, cmPtr, cm2Ptr, cm3Ptr, screenPoint);
            }
            finally
            {
                if (cm3Ptr != IntPtr.Zero) Marshal.Release(cm3Ptr);
                if (cm2Ptr != IntPtr.Zero) Marshal.Release(cm2Ptr);
                if (cmPtr != IntPtr.Zero) Marshal.Release(cmPtr);
                if (folderPtr != IntPtr.Zero) Marshal.Release(folderPtr);
                FreePidls(childPidls, cidl);
                Shell32.SHFree(pidlFull);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ContextMenuService.ShowContextMenu threw: {ex}");
        }
    }

    private static void Show(IntPtr hwnd, IContextMenu cm, IntPtr cmPtr, IntPtr cm2Ptr, IntPtr cm3Ptr, PixelPoint screenPoint)
    {
        var hmenu = Shell32.CreatePopupMenu();
        if (hmenu == IntPtr.Zero) return;

        try
        {
            var hrQ = cm.QueryContextMenu(hmenu, 0, IdCmdFirst, IdCmdLast, Shell32.CMF_NORMAL);
            if (hrQ < 0)
            {
                Debug.WriteLine($"ContextMenuService: QueryContextMenu returned 0x{hrQ:X8}");
                return;
            }

            var cmdId = Shell32.TrackPopupMenuEx(
                hmenu,
                Shell32.TPM_LEFTALIGN | Shell32.TPM_TOPALIGN | Shell32.TPM_RETURNCMD,
                screenPoint.X, screenPoint.Y, hwnd, IntPtr.Zero);

            if (cmdId == 0) return; // dismissed

            var offset = (uint)(cmdId - IdCmdFirst);
            var ici = new CMINVOKECOMMANDINFO
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                fMask = 0,
                hwnd = IntPtr.Zero,
                // verb-by-offset: HIWORD == 0 ⇒ IContextMenu treats LOWORD as the verb offset.
                lpVerb = (IntPtr)offset,
                lpParameters = IntPtr.Zero,
                lpDirectory = IntPtr.Zero,
                nShow = Shell32.SW_SHOWNORMAL
            };

            var hrInv = cm.InvokeCommand(ref ici);
            if (hrInv < 0)
            {
                Debug.WriteLine($"ContextMenuService: InvokeCommand returned 0x{hrInv:X8}");
            }

            _ = cm2Ptr; _ = cm3Ptr; // reserved for WM_INITMENUPOPUP subclassing (see IContextMenu2/3::HandleMenuMsg).
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ContextMenuService.Show threw: {ex}");
        }
        finally
        {
            Shell32.DestroyMenu(hmenu);
        }
    }

    private static void FreePidls(IntPtr[]? pidls, int count)
    {
        if (pidls is null) return;
        for (var i = 0; i < count; i++)
        {
            if (pidls[i] != IntPtr.Zero)
            {
                Shell32.SHFree(pidls[i]);
                pidls[i] = IntPtr.Zero;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}