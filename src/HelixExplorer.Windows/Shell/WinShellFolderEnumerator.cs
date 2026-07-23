using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Windows.FileSystem;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Windows.Shell;

public sealed class WinShellFolderEnumerator : IShellFolderEnumerator, IDisposable
{
    private const uint ShgdnForParsing = 0x8000;
    private const uint ShgdnNormal = 0;
    private const uint SfgaoFolder = 0x00000010;
    private const uint ShcontFolder = 0x10;
    private const uint ShcontNonFolder = 0x40;

    private readonly ILogger<WinShellFolderEnumerator> _logger;
    private readonly RecycleBinWatcher _recycleBinWatcher = new();

    public WinShellFolderEnumerator(ILogger<WinShellFolderEnumerator> logger)
    {
        _logger = logger;
        _recycleBinWatcher.Changed += (_, _) => RecycleBinChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask<IReadOnlyList<FileSystemEntry>> EnumerateAsync(string shellPath, CancellationToken ct = default)
        => await STATask.Run(() => Enumerate(shellPath, ct), ct).ConfigureAwait(false);

    public async ValueTask RestoreAsync(string itemPath, string? destinationPath = null, CancellationToken ct = default)
    {
        destinationPath ??= ReadRecycleBinMetadata(itemPath)?.OriginalPath;
        if (string.IsNullOrEmpty(destinationPath))
            throw new InvalidOperationException($"Could not determine original path for '{itemPath}'.");

        var success = await ShellFileOperationsHelper.RestoreFromRecycleBinAsync(
            itemPath, destinationPath, ct).ConfigureAwait(false);

        if (!success)
            throw new InvalidOperationException($"Restore failed for '{itemPath}'.");
    }

    public async ValueTask EmptyRecycleBinAsync(CancellationToken ct = default)
    {
        await STATask.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            Shell32Native.SHEmptyRecycleBin(
                IntPtr.Zero,
                null,
                Shell32Native.SHERB_NOCONFIRMATION | Shell32Native.SHERB_NOPROGRESSUI);
        }, ct).ConfigureAwait(false);
    }

    public async ValueTask<(long ItemCount, long TotalSize)> QueryRecycleBinAsync(CancellationToken ct = default)
    {
        return await STATask.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var info = new SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>() };
            var hr = Shell32Native.SHQueryRecycleBin(null, ref info);
            return hr == 0
                ? (info.i64NumItems, info.i64Size)
                : (0L, 0L);
        }, ct).ConfigureAwait(false);
    }

    public bool HasRecycleBinItems()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrEmpty(sid))
            return false;

        foreach (var drive in System.IO.DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType == System.IO.DriveType.Network)
                continue;

            var recyclePath = System.IO.Path.Combine(drive.RootDirectory.FullName, "$RECYCLE.BIN", sid);
            if (!System.IO.Directory.Exists(recyclePath))
                continue;

            try
            {
                if (System.IO.Directory.EnumerateFiles(recyclePath, "$I*", System.IO.SearchOption.TopDirectoryOnly).Any())
                    return true;
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }

        return false;
    }

    public event EventHandler? RecycleBinChanged;

    public void StartRecycleBinWatcher() => _recycleBinWatcher.Start();

    public void StopRecycleBinWatcher() => _recycleBinWatcher.Stop();

    public void Dispose() => _recycleBinWatcher.Dispose();

    private IReadOnlyList<FileSystemEntry> Enumerate(string shellPath, CancellationToken ct)
    {
        var entries = new List<FileSystemEntry>();
        var isRecycleBin = ShellPath.IsRecycleBin(shellPath);

        if (!Shell32Native.TryGetDesktopFolder(out var desktop) || desktop is null)
            return entries;

        // The desktop shell folder is a COM object we own; guarantee its release even on early exits.
        try
        {
            uint attr = 0;
            var hr = desktop.ParseDisplayName(IntPtr.Zero, IntPtr.Zero, shellPath, 0, out var pidlFolder, ref attr);
            if (hr != 0 || pidlFolder == IntPtr.Zero)
            {
                if (pidlFolder != IntPtr.Zero)
                    Shell32Native.SHFree(pidlFolder);
                return entries;
            }

            var folderPtr = IntPtr.Zero;
            var enumPtr = IntPtr.Zero;
            IShellFolder? folder = null;
            IEnumIDList? enumIdList = null;
            try
            {
                var iid = ShellIID.IID_IShellFolder;
                if (desktop.BindToObject(pidlFolder, IntPtr.Zero, ref iid, out folderPtr) != 0 || folderPtr == IntPtr.Zero)
                    return entries;

                folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPtr);
                if (folder.EnumObjects(IntPtr.Zero, ShcontFolder | ShcontNonFolder, out enumPtr) != 0 || enumPtr == IntPtr.Zero)
                    return entries;

                enumIdList = (IEnumIDList)Marshal.GetObjectForIUnknown(enumPtr);
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
                        if (TryMapEntry(folder, childPidl, isRecycleBin, out var entry))
                            entries.Add(entry);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Skipping failed shell entry");
                    }
                    finally
                    {
                        Shell32Native.SHFree(childPidl);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Shell enumerate failed for '{ShellPath}'", shellPath);
            }
            finally
            {
                if (enumIdList is not null)
                    Marshal.ReleaseComObject(enumIdList);
                if (enumPtr != IntPtr.Zero)
                    Marshal.Release(enumPtr);
                if (folder is not null)
                    Marshal.ReleaseComObject(folder);
                if (folderPtr != IntPtr.Zero)
                    Marshal.Release(folderPtr);
                Shell32Native.SHFree(pidlFolder);
            }

            entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return entries;
        }
        finally
        {
            Marshal.ReleaseComObject(desktop);
        }
    }

    private static bool TryMapEntry(IShellFolder folder, IntPtr pidl, bool isRecycleBin, out FileSystemEntry entry)
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
        var hr = folder.GetAttributesOf(1, apidl, ref attributes);
        var isDir = hr == 0
            ? (attributes & SfgaoFolder) != 0
            : parsingName.IndexOfAny(['\\', '/']) < 0;

        entry = new FileSystemEntry(
            parsingName,
            displayName,
            isDir,
            0,
            DateTime.MinValue,
            isDir ? string.Empty : Path.GetExtension(displayName),
            IsHidden: false);

        if (isRecycleBin)
            entry = EnrichRecycleBinEntry(entry, parsingName);

        return true;
    }

    private static FileSystemEntry EnrichRecycleBinEntry(FileSystemEntry entry, string recyclePath)
    {
        if (string.IsNullOrWhiteSpace(recyclePath))
            return entry;

        var metadata = ReadRecycleBinMetadata(recyclePath);
        if (metadata is null)
            return entry;

        var (size, deletedAt, originalPath) = metadata.Value;
        return entry with
        {
            SizeBytes = size,
            ModifiedUtc = deletedAt,
            OriginalPath = originalPath,
            DeletedAtUtc = deletedAt
        };
    }

    private static (long Size, DateTime DeletedAtUtc, string OriginalPath)? ReadRecycleBinMetadata(string rPath)
    {
        var fileName = Path.GetFileName(rPath);
        if (string.IsNullOrEmpty(fileName) || !fileName.StartsWith("$R", StringComparison.Ordinal))
            return null;

        var directory = Path.GetDirectoryName(rPath);
        if (string.IsNullOrEmpty(directory))
            return null;

        var iFileName = "$I" + fileName.Substring(2);
        var iPath = Path.Combine(directory, iFileName);
        if (!File.Exists(iPath))
            return null;

        return RecycleBinMetadataParser.TryParseFile(iPath);
    }

    /// <summary>
    /// Reusable buffer for <see cref="GetDisplayName"/>. Avoids allocating a 64 KB
    /// <see cref="StringBuilder"/> per entry, which caused significant GC pressure when
    /// enumerating large directories.
    /// </summary>
    [ThreadStatic]
    private static StringBuilder? t_displayNameBuffer;

    private static string GetDisplayName(IShellFolder folder, IntPtr pidl, uint flags)
    {
        var strret = new STRRET();
        var hr = folder.GetDisplayNameOf(pidl, flags, out strret);
        if (hr != 0)
            return string.Empty;

        // Legacy MAX_PATH (260) truncates long paths; use the extended maximum (~32K WCHARs).
        const int extendedMaxPath = 32768;
        var sb = t_displayNameBuffer ??= new StringBuilder(extendedMaxPath);
        sb.Clear();
        Shell32Native.StrRetToBuf(ref strret, pidl, sb, extendedMaxPath);
        return sb.ToString();
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
