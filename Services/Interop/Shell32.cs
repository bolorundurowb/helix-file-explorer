using System.Runtime.InteropServices;

namespace HelixExplorer.Services.Interop;

internal static class Shell32
{
    // Desktop IShellFolder.
    [DllImport("shell32.dll")]
    public static extern int SHGetDesktopFolder(out IShellFolder ppshf);

    // Task allocator used to free PIDLs.
    [DllImport("shell32.dll")]
    public static extern void SHFree(IntPtr pidl);

    // Popup menu tracking with positioning.
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int TrackPopupMenuEx(
        IntPtr hmenu, uint uFlags, int x, int y,
        IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    /// <summary>Tristate used by <see cref="TrackPopupMenuEx"/> for window subclassing.</summary>
    public const int TPM_RETURNCMD = 0x0100;
    public const int TPM_LEFTALIGN = 0x0000;
    public const int TPM_TOPALIGN = 0x0000;

    public const int WM_INITMENUPOPUP = 0x0117;
    public const int WM_DRAWITEM = 0x002C;
    public const int WM_MEASUREITEM = 0x0024;

    public const int CMF_NORMAL = 0x00000000;
    public const int CMF_DEFAULTONLY = 0x00000001;
    public const int CMF_VERBSONLY = 0x00000002;

    public const int GCS_VERBA = 0x0000;
    public const int GCS_VERBW = 0x0004;

    public const int SW_SHOWNORMAL = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct TPMPARAMS
    {
        public int cbSize;
        public RECT rcExclude;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
}