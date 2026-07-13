using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Windows.Shell;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Windows.Shell;

public sealed class WinShellFolderEnumerator(ILogger<WinShellFolderEnumerator> logger) : IShellFolderEnumerator
{
    private const uint ShgdnForParsing = 0x8000;
    private const uint ShgdnNormal = 0;
    private const uint SfgaoFolder = 0x00000010;
    private const uint ShcontFolder = 0x10;
    private const uint ShcontNonFolder = 0x40;

    public async ValueTask<IReadOnlyList<FileSystemEntry>> EnumerateAsync(string shellPath, CancellationToken ct = default)
        => await Task.Run(() => Enumerate(shellPath, ct), ct).ConfigureAwait(false);

    public async ValueTask RestoreAsync(string itemPath, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var info = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                fMask = Shell32Native.SEE_MASK_INVOKEIDLIST,
                lpVerb = "undelete",
                lpFile = itemPath,
                nShow = Shell32Native.SW_SHOWNORMAL
            };
            if (!Shell32Native.ShellExecuteEx(ref info))
                throw new InvalidOperationException($"Restore failed for '{itemPath}'.");
        }, ct).ConfigureAwait(false);
    }

    public async ValueTask EmptyRecycleBinAsync(CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var info = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                fMask = Shell32Native.SEE_MASK_INVOKEIDLIST,
                lpVerb = "empty",
                lpFile = ShellPath.RecycleBin,
                nShow = Shell32Native.SW_SHOWNORMAL
            };
            Shell32Native.ShellExecuteEx(ref info);
        }, ct).ConfigureAwait(false);
    }

    private IReadOnlyList<FileSystemEntry> Enumerate(string shellPath, CancellationToken ct)
    {
        var entries = new List<FileSystemEntry>();
        if (Shell32Native.SHGetDesktopFolder(out var desktop) != 0)
            return entries;

        uint attr = 0;
        var hr = desktop.ParseDisplayName(IntPtr.Zero, IntPtr.Zero, shellPath, 0, out var pidlFolder, ref attr);
        if (hr != 0 || pidlFolder == IntPtr.Zero)
            return entries;

        var folderPtr = IntPtr.Zero;
        var enumPtr = IntPtr.Zero;
        try
        {
            var iid = ShellIID.IID_IShellFolder;
            if (desktop.BindToObject(pidlFolder, IntPtr.Zero, ref iid, out folderPtr) != 0 || folderPtr == IntPtr.Zero)
                return entries;

            var folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPtr);
            if (folder.EnumObjects(IntPtr.Zero, ShcontFolder | ShcontNonFolder, out enumPtr) != 0 || enumPtr == IntPtr.Zero)
                return entries;

            var enumIdList = (IEnumIDList)Marshal.GetObjectForIUnknown(enumPtr);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var childPidl = IntPtr.Zero;
                var fetched = 0;
                var next = enumIdList.Next(1, out childPidl, ref fetched);
                if (next != 0 || fetched == 0 || childPidl == IntPtr.Zero)
                    break;

                try
                {
                    if (TryMapEntry(folder, childPidl, out var entry))
                        entries.Add(entry);
                }
                finally
                {
                    Shell32Native.SHFree(childPidl);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Shell enumerate failed for '{ShellPath}'", shellPath);
        }
        finally
        {
            if (enumPtr != IntPtr.Zero)
                Marshal.Release(enumPtr);
            if (folderPtr != IntPtr.Zero)
                Marshal.Release(folderPtr);
            Shell32Native.SHFree(pidlFolder);
        }

        entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return entries;
    }

    private static bool TryMapEntry(IShellFolder folder, IntPtr pidl, out FileSystemEntry entry)
    {
        entry = default;
        var parsingName = GetDisplayName(folder, pidl, ShgdnForParsing);
        var displayName = GetDisplayName(folder, pidl, ShgdnNormal);
        if (string.IsNullOrEmpty(parsingName))
            parsingName = displayName;
        if (string.IsNullOrEmpty(parsingName))
            return false;

        uint attributes = SfgaoFolder;
        var apidl = new[] { pidl };
        folder.GetAttributesOf(1, apidl, ref attributes);
        var isDir = (attributes & SfgaoFolder) != 0;

        entry = new FileSystemEntry(
            parsingName,
            displayName,
            isDir,
            0,
            DateTime.MinValue,
            isDir ? string.Empty : Path.GetExtension(displayName),
            IsHidden: false);
        return true;
    }

    private static string GetDisplayName(IShellFolder folder, IntPtr pidl, uint flags)
    {
        var hr = folder.GetDisplayNameOf(pidl, flags, out var strPtr);
        if (hr != 0 || strPtr == IntPtr.Zero)
            return string.Empty;

        try
        {
            return Marshal.PtrToStringUni(strPtr) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeCoTaskMem(strPtr);
        }
    }
}

[ComImport]
[Guid("000214F2-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumIDList
{
    [PreserveSig]
    int Next(uint celt, out IntPtr rgelt, ref int pceltFetched);

    [PreserveSig]
    int Skip(uint celt);

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int Clone(out IEnumIDList ppenum);
}
