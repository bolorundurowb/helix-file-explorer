using System.Runtime.InteropServices;

namespace HelixExplorer.Windows.Shell;

internal static class ShellIID
{
    public static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    public static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");
    public static readonly Guid IID_IContextMenu2 = new("000214F4-0000-0000-C000-000000000046");
    public static readonly Guid IID_IContextMenu3 = new("BCFCE0C0-EC17-11D0-8D10-00A0C90F2719");
}

internal static class Shell32Native
{
    public const uint CMF_NORMAL = 0;
    public const uint TPM_LEFTALIGN = 0x0000;
    public const uint TPM_TOPALIGN = 0x0000;
    public const uint TPM_RETURNCMD = 0x0100;
    public const int SW_SHOWNORMAL = 1;
    public const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;

    [DllImport("shell32.dll")]
    public static extern int SHGetDesktopFolder(out IShellFolder ppshf);

    [DllImport("shell32.dll")]
    public static extern void SHFree(IntPtr pidl);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int TrackPopupMenuEx(IntPtr hmenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);
}

[ComImport]
[Guid("000214E6-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellFolder
{
    [PreserveSig]
    int ParseDisplayName(
        IntPtr hwnd,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
        uint pchEaten,
        out IntPtr ppidl,
        ref uint pdwAttributes);

    [PreserveSig]
    int EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);

    [PreserveSig]
    int BindToObject(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int BindToStorage(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);

    [PreserveSig]
    int CreateViewObject(IntPtr hwndOwner, [In] ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int GetAttributesOf(uint cidl, IntPtr[]? apidl, ref uint rgfInOut);

    [PreserveSig]
    int GetUIObjectOf(
        IntPtr hwndOwner,
        uint cidl,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[]? apidl,
        [In] ref Guid riid,
        ref uint rgfReserved,
        out IntPtr ppv);

    [PreserveSig]
    int GetDisplayNameOf(IntPtr pidl, uint uFlags, out IntPtr pName);

    [PreserveSig]
    int SetNameOf(
        IntPtr hwndOwner,
        IntPtr pidl,
        [MarshalAs(UnmanagedType.LPWStr)] string pszName,
        uint uFlags,
        out IntPtr ppidlOut);
}

[ComImport]
[Guid("000214E4-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu
{
    [PreserveSig]
    int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);

    [PreserveSig]
    int InvokeCommand(ref CMINVOKECOMMANDINFO pici);

    [PreserveSig]
    int GetCommandString(uint idCmd, uint uType, IntPtr pReserved, out string pName, uint cchMax);
}

[StructLayout(LayoutKind.Sequential)]
internal struct CMINVOKECOMMANDINFO
{
    public int cbSize;
    public uint fMask;
    public IntPtr hwnd;
    public IntPtr lpVerb;
    public IntPtr lpParameters;
    public IntPtr lpDirectory;
    public int nShow;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct SHELLEXECUTEINFO
{
    public int cbSize;
    public uint fMask;
    public IntPtr hwnd;
    public string? lpVerb;
    public string? lpFile;
    public string? lpParameters;
    public string? lpDirectory;
    public int nShow;
    public IntPtr hInstApp;
    public IntPtr lpIDList;
    public string? lpClass;
    public IntPtr hkeyClass;
    public uint dwHotKey;
    public IntPtr hIconOrMonitor;
    public IntPtr hProcess;
}
