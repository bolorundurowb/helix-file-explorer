using System.Runtime.InteropServices;
using HelixExplorer.Core.FileSystem;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static Vanara.PInvoke.Kernel32;
using static Vanara.PInvoke.Shell32;
using static Vanara.PInvoke.User32;

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
            ShowContextMenu(ownerHwnd, folderPath, paths, screenX, screenY);
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
                fMask = ShellExecuteMaskFlags.SEE_MASK_INVOKEIDLIST,
                hwnd = ownerHwnd,
                lpVerb = "properties",
                lpFile = path,
                nShellExecuteShow = ShowWindowCommand.SW_SHOWNORMAL
            };

            if (!ShellExecuteEx(ref info))
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
        HWND hwnd,
        string folderPath,
        IReadOnlyList<string> selectedPaths,
        int screenX,
        int screenY)
    {
        var hrDesktop = SHGetDesktopFolder(out var desktop);
        if (hrDesktop.Failed || desktop is null)
            return;

        var hr = desktop.ParseDisplayName(HWND.NULL, null, folderPath, out _, out var pidlFull, IntPtr.Zero);
        if (hr.Failed || pidlFull is null || pidlFull.IsInvalid)
        {
            logger.LogError("ParseDisplayName failed for '{FolderPath}': 0x{Hr:X8}", folderPath, (int)hr);
            pidlFull?.Dispose();
            Marshal.ReleaseComObject(desktop);
            return;
        }

        IShellFolder? folder = null;
        IContextMenu? cm = null;
        var childPidls = new List<PIDL>();

        try
        {
            var iidShellFolder = typeof(IShellFolder).GUID;
            hr = desktop.BindToObject(pidlFull, null, in iidShellFolder, out var folderObj);
            if (hr.Failed || folderObj is not IShellFolder boundFolder)
            {
                logger.LogError("BindToObject failed for '{FolderPath}': 0x{HrBind:X8}", folderPath, (int)hr);
                return;
            }

            folder = boundFolder;

            if (selectedPaths.Count > 0)
            {
                foreach (var path in selectedPaths)
                {
                    if (string.IsNullOrEmpty(path))
                        continue;

                    var fileName = Path.GetFileName(path.TrimEnd('\\', '/'));
                    if (string.IsNullOrEmpty(fileName))
                        continue;

                    var hrFile = folder.ParseDisplayName(
                        HWND.NULL,
                        null,
                        fileName,
                        out _,
                        out var childPidl,
                        IntPtr.Zero);
                    if (hrFile.Succeeded && childPidl is not null && !childPidl.IsInvalid)
                        childPidls.Add(childPidl);
                    else
                        logger.LogDebug("ParseDisplayName failed for child '{Path}': 0x{HrFile:X8}", path, (int)hrFile);
                }
            }

            var iidCm = typeof(IContextMenu).GUID;
            object? cmObj;
            if (childPidls.Count > 0)
            {
                var apidl = childPidls.Select(p => (IntPtr)p).ToArray();
                hr = folder.GetUIObjectOf(hwnd, (uint)apidl.Length, apidl, in iidCm, IntPtr.Zero, out cmObj);
                if (hr.Failed || cmObj is not IContextMenu)
                {
                    logger.LogError("GetUIObjectOf failed for {Count} item(s): 0x{HrCm:X8}", childPidls.Count, (int)hr);
                    return;
                }
            }
            else
            {
                // GetUIObjectOf(cidl=0) does not yield the background menu on most shell namespaces;
                // CreateViewObject is the documented pattern for the folder's own background verbs
                // (Defender/7-Zip/etc.).
                hr = folder.CreateViewObject(hwnd, in iidCm, out cmObj);
                if (hr.Failed || cmObj is not IContextMenu)
                {
                    logger.LogError("CreateViewObject failed for '{FolderPath}': 0x{HrCv:X8}", folderPath, (int)hr);
                    return;
                }
            }

            cm = (IContextMenu)cmObj;
            TrackAndInvoke(hwnd, cm, screenX, screenY);
        }
        finally
        {
            if (cm is not null)
                Marshal.ReleaseComObject(cm);
            if (folder is not null)
                Marshal.ReleaseComObject(folder);
            foreach (var pidl in childPidls)
                pidl.Dispose();
            pidlFull.Dispose();
            Marshal.ReleaseComObject(desktop);
        }
    }

    private void TrackAndInvoke(HWND hwnd, IContextMenu cm, int screenX, int screenY)
    {
        var hmenu = CreatePopupMenu();
        if (hmenu.IsNull)
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

        HHOOK hook = default;
        HookProc? hookProc = null;
        try
        {
            var hrQ = cm.QueryContextMenu(hmenu, 0, IdCmdFirst, IdCmdLast, CMF.CMF_NORMAL);
            if (hrQ.Failed)
            {
                logger.LogError("QueryContextMenu returned 0x{HrQ:X8}", (int)hrQ);
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
                    if (code >= 0 && wParam == (IntPtr)MSGF.MSGF_MENU)
                    {
                        var msg = Marshal.PtrToStructure<MSG>(lParam);
                        if (IsContextMenuMessage(msg.message))
                        {
                            if (cm3 is not null)
                                cm3.HandleMenuMsg2(msg.message, msg.wParam, msg.lParam, out _);
                            else
                                cm2!.HandleMenuMsg(msg.message, msg.wParam, msg.lParam);
                        }
                    }

                    return CallNextHookEx(hook, code, wParam, lParam);
                };

                hook = SetWindowsHookEx(
                    HookType.WH_MSGFILTER,
                    hookProc,
                    HINSTANCE.NULL,
                    (int)GetCurrentThreadId());
            }

            var owner = hwnd.IsNull ? GetDesktopWindow() : hwnd;
            var cmdId = TrackPopupMenuEx(
                hmenu,
                TrackPopupMenuFlags.TPM_LEFTALIGN | TrackPopupMenuFlags.TPM_TOPALIGN | TrackPopupMenuFlags.TPM_RETURNCMD,
                screenX,
                screenY,
                owner,
                null!);

            if (cmdId == 0)
                return;

            var offset = (int)(cmdId - IdCmdFirst);
            var ici = new CMINVOKECOMMANDINFOEX(
                offset,
                ShowWindowCommand.SW_SHOWNORMAL,
                hwnd);

            var iciPtr = Marshal.AllocHGlobal(Marshal.SizeOf<CMINVOKECOMMANDINFOEX>());
            try
            {
                Marshal.StructureToPtr(ici, iciPtr, false);
                var hrInv = cm.InvokeCommand(iciPtr);
                if (hrInv.Failed)
                    logger.LogError("InvokeCommand returned 0x{HrInv:X8}", (int)hrInv);
            }
            finally
            {
                Marshal.DestroyStructure<CMINVOKECOMMANDINFOEX>(iciPtr);
                Marshal.FreeHGlobal(iciPtr);
            }
        }
        finally
        {
            if (!hook.IsNull)
                UnhookWindowsHookEx(hook);
            GC.KeepAlive(hookProc);
            DestroyMenu(hmenu);
        }
    }

    private static bool IsContextMenuMessage(uint message)
        => message is (uint)WindowMessage.WM_INITMENUPOPUP
            or (uint)WindowMessage.WM_MEASUREITEM
            or (uint)WindowMessage.WM_DRAWITEM
            or (uint)WindowMessage.WM_MENUCHAR;
}
