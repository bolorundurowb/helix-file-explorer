using System.Runtime.InteropServices;

namespace HelixExplorer.Windows.Shell;

[Flags]
internal enum ShellGetFileInfoFlags : uint
{
    Icon = 0x000000100,
    LargeIcon = 0x0,
    SmallIcon = 0x1,
    UseFileAttributes = 0x000000010,
}

[Flags]
internal enum ShellFileAttributes : uint
{
    Directory = 0x10,
    Normal = 0x80,
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct ShellFileInfo
{
    public IntPtr hIcon;
    public int iIcon;
    public uint dwAttributes;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string szDisplayName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
    public string szTypeName;
}

internal static class ShellIconNative
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SHGetFileInfo(
        string pszPath,
        ShellFileAttributes dwFileAttributes,
        ref ShellFileInfo psfi,
        uint cbFileInfo,
        ShellGetFileInfoFlags uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
