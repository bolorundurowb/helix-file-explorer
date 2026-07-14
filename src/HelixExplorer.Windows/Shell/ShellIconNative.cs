using System.Runtime.InteropServices;

namespace HelixExplorer.Windows.Shell;

[Flags]
internal enum ShellGetFileInfoFlags : uint
{
    Icon = 0x000000100,
    LargeIcon = 0x0,
    SmallIcon = 0x1,
    SysIconIndex = 0x000004000,
    UseFileAttributes = 0x000000010,
}

internal enum ShellImageListSize
{
    Large = 0,
    Small = 1,
    ExtraLarge = 2,
    SystemSmall = 3,
    Jumbo = 4
}

[Flags]
internal enum ImageListDrawFlags : uint
{
    Transparent = 0x00000001
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
    public static readonly Guid IID_IImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SHGetFileInfo(
        string pszPath,
        ShellFileAttributes dwFileAttributes,
        ref ShellFileInfo psfi,
        uint cbFileInfo,
        ShellGetFileInfoFlags uFlags);

    [DllImport("shell32.dll", EntryPoint = "#727")]
    public static extern int SHGetImageList(
        ShellImageListSize iImageList,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IImageList? ppv);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}

[ComImport]
[Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IImageList
{
    [PreserveSig]
    int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);

    [PreserveSig]
    int ReplaceIcon(int i, IntPtr hicon, ref int pi);

    [PreserveSig]
    int SetOverlayImage(int iImage, int iOverlay);

    [PreserveSig]
    int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);

    [PreserveSig]
    int AddMasked(IntPtr hbmImage, int crMask, ref int pi);

    [PreserveSig]
    int Draw(IntPtr pimldp);

    [PreserveSig]
    int Remove(int i);

    [PreserveSig]
    int GetIcon(int i, ImageListDrawFlags flags, ref IntPtr picon);
}
