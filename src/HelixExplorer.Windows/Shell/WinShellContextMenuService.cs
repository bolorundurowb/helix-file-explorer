using System.Runtime.InteropServices;
using HelixExplorer.Core.FileSystem;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Windows.Shell;

/// <summary>
/// Explorer-compatible shell context menu. COM failures must not break Helix's own menus.
/// </summary>
public sealed class WinShellContextMenuService(ILogger<WinShellContextMenuService> logger) : IShellContextMenuService
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
            logger.LogError(ex, "ShowMoreOptions failed");
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
                logger.LogError("ShellExecuteEx(properties) failed with error {Error}", error);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ShowProperties failed");
        }

        return ValueTask.CompletedTask;
    }

    private void ShowContextMenu(
        IntPtr hwnd,
        string folderPath,
        IReadOnlyList<string> selectedPaths,
        int screenX,
        int screenY)
    {
        if (!Shell32Native.TryGetDesktopFolder(out var desktop) || desktop is null)
            return;

        uint attr = 0;
        var hr = desktop.ParseDisplayName(IntPtr.Zero, IntPtr.Zero, folderPath, 0, out var pidlFull, ref attr);
        if (hr != 0 || pidlFull == IntPtr.Zero)
        {
            logger.LogError("ParseDisplayName failed for '{FolderPath}': 0x{Hr:X8}", folderPath, hr);
            if (pidlFull != IntPtr.Zero)
                Shell32Native.SHFree(pidlFull);
            Marshal.ReleaseComObject(desktop);
            return;
        }

        IShellFolder? folder = null;
        var folderPtr = IntPtr.Zero;
        IContextMenu? cm = null;
        var cmPtr = IntPtr.Zero;
        IntPtr[]? childPidls = null;
        var cidl = 0;

        try
        {
            var iidShellFolder = ShellIID.IID_IShellFolder;
            var hrBind = desktop.BindToObject(pidlFull, IntPtr.Zero, ref iidShellFolder, out folderPtr);
            if (hrBind != 0 || folderPtr == IntPtr.Zero)
            {
                logger.LogError("BindToObject failed for '{FolderPath}': 0x{HrBind:X8}", folderPath, hrBind);
                return;
            }

            folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPtr);

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
                    else
                        logger.LogDebug("ParseDisplayName failed for child '{Path}': 0x{HrFile:X8}", path, hrFile);
                }
            }

            if (cidl > 0)
            {
                var apidl = childPidls![..cidl];
                var iidCm = ShellIID.IID_IContextMenu;
                uint reserved = 0;
                var hrCm = folder.GetUIObjectOf(hwnd, (uint)cidl, apidl, ref iidCm, ref reserved, out cmPtr);
                if (hrCm != 0 || cmPtr == IntPtr.Zero)
                {
                    logger.LogError("GetUIObjectOf failed for {Count} item(s): 0x{HrCm:X8}", cidl, hrCm);
                    return;
                }
            }
            else
            {
                // GetUIObjectOf(cidl=0) does not yield the background menu on most shell namespaces;
                // CreateViewObject is the documented pattern for the folder's own background verbs
                // (Defender/7-Zip/etc.).
                var iidCm = ShellIID.IID_IContextMenu;
                var hrCv = folder.CreateViewObject(hwnd, ref iidCm, out cmPtr);
                if (hrCv != 0 || cmPtr == IntPtr.Zero)
                {
                    logger.LogError("CreateViewObject failed for '{FolderPath}': 0x{HrCv:X8}", folderPath, hrCv);
                    return;
                }
            }

            cm = (IContextMenu)Marshal.GetObjectForIUnknown(cmPtr);
            TrackAndInvoke(hwnd, cm, screenX, screenY);
        }
        finally
        {
            if (cm is not null)
                Marshal.ReleaseComObject(cm);
            if (cmPtr != IntPtr.Zero)
                Marshal.Release(cmPtr);
            if (folder is not null)
                Marshal.ReleaseComObject(folder);
            if (folderPtr != IntPtr.Zero)
                Marshal.Release(folderPtr);
            FreePidls(childPidls, cidl);
            Shell32Native.SHFree(pidlFull);
            Marshal.ReleaseComObject(desktop);
        }
    }

    private void TrackAndInvoke(IntPtr hwnd, IContextMenu cm, int screenX, int screenY)
    {
        var hmenu = Shell32Native.CreatePopupMenu();
        if (hmenu == IntPtr.Zero)
            return;

        IContextMenu2? cm2 = null;
        IContextMenu3? cm3 = null;
        try
        {
            cm3 = cm as IContextMenu3;
            cm2 = cm as IContextMenu2;
        }
        catch (InvalidCastException)
        {
            // Optional extensions; owner-drawn submenus simply won't paint.
        }

        IntPtr hook = IntPtr.Zero;
        NativeMethods.HookProc? hookProc = null;
        try
        {
            var hrQ = cm.QueryContextMenu(hmenu, 0, IdCmdFirst, IdCmdLast, Shell32Native.CMF_NORMAL);
            if (hrQ < 0)
            {
                logger.LogError("QueryContextMenu returned 0x{HrQ:X8}", hrQ);
                return;
            }

            if (screenX == 0 && screenY == 0)
            {
                GetCursorPos(out var pt);
                screenX = pt.X;
                screenY = pt.Y;
            }

            // Cascading/owner-drawn shell verbs only paint if we forward menu messages while the popup is open.
            if (cm2 is not null || cm3 is not null)
            {
                hookProc = (code, wParam, lParam) =>
                {
                    if (code >= 0 && wParam == (IntPtr)NativeMethods.MSGF_MENU)
                    {
                        var msg = Marshal.PtrToStructure<NativeMethods.MSG>(lParam);
                        if (IsContextMenuMessage(msg.message))
                        {
                            if (cm3 is not null)
                                cm3.HandleMenuMsg2(msg.message, msg.wParam, msg.lParam, out _);
                            else
                                cm2!.HandleMenuMsg(msg.message, msg.wParam, msg.lParam);
                        }
                    }

                    return NativeMethods.CallNextHookEx(hook, code, wParam, lParam);
                };

                hook = NativeMethods.SetWindowsHookEx(
                    NativeMethods.WH_MSGFILTER,
                    hookProc,
                    IntPtr.Zero,
                    NativeMethods.GetCurrentThreadId());
            }

            var owner = hwnd == IntPtr.Zero ? GetDesktopWindow() : hwnd;
            var cmdId = Shell32Native.TrackPopupMenuEx(
                hmenu,
                Shell32Native.TPM_LEFTALIGN | Shell32Native.TPM_TOPALIGN | Shell32Native.TPM_RETURNCMD,
                screenX,
                screenY,
                owner,
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
                logger.LogError("InvokeCommand returned 0x{HrInv:X8}", hrInv);
        }
        finally
        {
            if (hook != IntPtr.Zero)
                NativeMethods.UnhookWindowsHookEx(hook);
            GC.KeepAlive(hookProc);
            Shell32Native.DestroyMenu(hmenu);
        }
    }

    private static bool IsContextMenuMessage(uint message)
        => message is NativeMethods.WM_INITMENUPOPUP
            or NativeMethods.WM_MEASUREITEM
            or NativeMethods.WM_DRAWITEM
            or NativeMethods.WM_MENUCHAR;

    private static class NativeMethods
    {
        public const int WH_MSGFILTER = -1;
        public const int MSGF_MENU = 2;
        public const uint WM_INITMENUPOPUP = 0x0117;
        public const uint WM_MEASUREITEM = 0x002C;
        public const uint WM_DRAWITEM = 0x002B;
        public const uint WM_MENUCHAR = 0x0120;

        public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int pt_x;
            public int pt_y;
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
