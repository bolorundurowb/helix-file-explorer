using System.Runtime.InteropServices;

namespace HelixExplorer.Windows.Shell;

internal static class ShellImageIid
{
    public static readonly Guid IShellItemImageFactory = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
}

[Flags]
internal enum Siigbf : uint
{
    ResizeToFit = 0x00,
    BiggerSizeOk = 0x01,
    MemoryOnly = 0x02,
    IconOnly = 0x04,
    ThumbnailOnly = 0x08,
    InCacheOnly = 0x10,
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSize
{
    public int cx;
    public int cy;
}

[ComImport]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    void GetImage(NativeSize size, Siigbf flags, out IntPtr phbm);
}

internal static class ShellImageNative
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    public static extern int SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        [In] ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);
}
