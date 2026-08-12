using System.Runtime.InteropServices;
using System.Security.Principal;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Windows.FileSystem;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static Vanara.PInvoke.Shell32;

namespace HelixExplorer.Windows.Shell;

public sealed class WinShellFolderEnumerator : IShellFolderEnumerator, IDisposable
{
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
            SHEmptyRecycleBin(
                HWND.NULL,
                null,
                SHERB.SHERB_NOCONFIRMATION | SHERB.SHERB_NOPROGRESSUI);
        }, ct).ConfigureAwait(false);
    }

    public async ValueTask<(long ItemCount, long TotalSize)> QueryRecycleBinAsync(CancellationToken ct = default)
    {
        return await STATask.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var info = new SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>() };
            var hr = SHQueryRecycleBin(null, ref info);
            return hr.Succeeded
                ? (info.i64NumItems, info.i64Size)
                : (0L, 0L);
        }, ct).ConfigureAwait(false);
    }

    public bool HasRecycleBinItems()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrEmpty(sid))
            return false;

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType == DriveType.Network)
                continue;

            var recyclePath = Path.Combine(drive.RootDirectory.FullName, "$RECYCLE.BIN", sid);
            if (!Directory.Exists(recyclePath))
                continue;

            try
            {
                if (Directory.EnumerateFiles(recyclePath, "$I*", SearchOption.TopDirectoryOnly).Any())
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

        var hrDesktop = SHGetDesktopFolder(out var desktop);
        if (hrDesktop.Failed || desktop is null)
            return entries;

        // The desktop shell folder is a COM object we own; guarantee its release even on early exits.
        try
        {
            var hr = desktop.ParseDisplayName(HWND.NULL, null, shellPath, out _, out var pidlFolder, IntPtr.Zero);
            if (hr.Failed || pidlFolder is null || pidlFolder.IsInvalid)
                return entries;

            try
            {
                var iid = typeof(IShellFolder).GUID;
                hr = desktop.BindToObject(pidlFolder, null, in iid, out var folderObj);
                if (hr.Failed || folderObj is not IShellFolder folder)
                    return entries;

                try
                {
                    hr = folder.EnumObjects(
                        HWND.NULL,
                        SHCONTF.SHCONTF_FOLDERS | SHCONTF.SHCONTF_NONFOLDERS,
                        out var enumIdList);
                    if (hr.Failed || enumIdList is null)
                        return entries;

                    try
                    {
                        var childBuf = new IntPtr[1];
                        while (true)
                        {
                            ct.ThrowIfCancellationRequested();
                            var next = enumIdList.Next(1, childBuf, out var fetched);
                            if (next.Failed || fetched == 0 || childBuf[0] == IntPtr.Zero)
                                break;

                            using var childPidl = new PIDL(childBuf[0]);
                            try
                            {
                                if (TryMapEntry(folder, childPidl, isRecycleBin, out var entry))
                                    entries.Add(entry);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Skipping failed shell entry");
                            }
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(enumIdList);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(folder);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Shell enumerate failed for '{ShellPath}'", shellPath);
            }
            finally
            {
                pidlFolder.Dispose();
            }

            entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return entries;
        }
        finally
        {
            Marshal.ReleaseComObject(desktop);
        }
    }

    private static bool TryMapEntry(IShellFolder folder, PIDL pidl, bool isRecycleBin, out FileSystemEntry entry)
    {
        entry = default;
        var parsingName = GetDisplayName(folder, pidl, SHGDNF.SHGDN_FORPARSING);
        var displayName = GetDisplayName(folder, pidl, SHGDNF.SHGDN_NORMAL);
        if (string.IsNullOrEmpty(parsingName))
            parsingName = displayName;
        if (string.IsNullOrEmpty(parsingName))
            return false;

        var attributes = SFGAO.SFGAO_FOLDER;
        var apidl = new[] { (IntPtr)pidl };
        var hr = folder.GetAttributesOf(1, apidl, ref attributes);
        var isDir = hr.Succeeded
            ? (attributes & SFGAO.SFGAO_FOLDER) != 0
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
    /// Prefer Vanara's <see cref="STRRET"/> → string conversion over pinning for <c>StrRetToBuf</c>.
    /// </summary>
    private static string GetDisplayName(IShellFolder folder, PIDL pidl, SHGDNF flags)
    {
        var hr = folder.GetDisplayNameOf(pidl, flags, out var strret);
        if (hr.Failed)
            return string.Empty;

        try
        {
            return (string?)strret ?? string.Empty;
        }
        finally
        {
            strret.Free();
        }
    }
}
