using System.ComponentModel;
using System.Runtime.InteropServices;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Sorting;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static Vanara.PInvoke.Mpr;

namespace HelixExplorer.Windows.FileSystem;

/// <summary>
/// Enumerates SMB disk shares under a <c>\\server</c> root via WNet.
/// </summary>
internal static class WinNetworkShareEnumerator
{
    public static IReadOnlyList<FileSystemEntry> EnumerateShares(string serverRoot, ILogger logger, CancellationToken cancellationToken)
    {
        var normalized = NetworkPath.Normalize(serverRoot);
        if (!NetworkPath.IsServerRoot(normalized))
            return Array.Empty<FileSystemEntry>();

        var parent = new NETRESOURCE
        {
            dwDisplayType = 0,
            lpRemoteName = normalized
        };

        var openResult = WNetOpenEnum(
            NETRESOURCEScope.RESOURCE_GLOBALNET,
            NETRESOURCEType.RESOURCETYPE_DISK,
            0,
            parent,
            out var handle);
        if (openResult != Win32Error.ERROR_SUCCESS)
            throw CreateException(openResult, normalized);

        var results = new List<FileSystemEntry>();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var count = -1;
                var bufferSize = (uint)NetworkEnumBuffer.InitialSize;
                var buffer = Marshal.AllocHGlobal((int)bufferSize);
                try
                {
                    var attemptedSize = (int)bufferSize;
                    var result = WNetEnumResource(handle, ref count, buffer, ref bufferSize);

                    if (result == Win32Error.ERROR_MORE_DATA)
                    {
                        Marshal.FreeHGlobal(buffer);
                        bufferSize = (uint)NetworkEnumBuffer.Grow(attemptedSize, (int)bufferSize);
                        buffer = Marshal.AllocHGlobal((int)bufferSize);
                        count = -1;
                        result = WNetEnumResource(handle, ref count, buffer, ref bufferSize);
                    }

                    if (result == Win32Error.ERROR_NO_MORE_ITEMS)
                        break;

                    if (result != Win32Error.ERROR_SUCCESS)
                        throw CreateException(result, normalized);

                    var itemSize = Marshal.SizeOf<NETRESOURCE>();
                    for (var i = 0; i < count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var resource = Marshal.PtrToStructure<NETRESOURCE>(buffer + i * itemSize);
                        if (resource is null || resource.dwDisplayType != NETRESOURCEDisplayType.RESOURCEDISPLAYTYPE_SHARE)
                            continue;

                        var remoteName = resource.lpRemoteName;
                        if (string.IsNullOrWhiteSpace(remoteName))
                            continue;

                        var path = NetworkPath.Normalize(remoteName);
                        var name = Path.GetFileName(path.TrimEnd('\\'));
                        if (string.IsNullOrEmpty(name))
                            name = path;

                        results.Add(new FileSystemEntry(
                            path,
                            name,
                            IsDirectory: true,
                            SizeBytes: 0,
                            ModifiedUtc: DateTime.MinValue,
                            Extension: string.Empty));
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        finally
        {
            handle.Dispose();
        }

        cancellationToken.ThrowIfCancellationRequested();
        results.Sort(FileSystemEntryComparer.For(SortColumn.Name, descending: false));
        logger.LogDebug("Enumerated {Count} shares under '{Server}'", results.Count, normalized);
        return results;
    }

    public static bool IsAccessDenied(Win32Error errorCode)
        => errorCode == Win32Error.ERROR_ACCESS_DENIED || errorCode == Win32Error.ERROR_LOGON_FAILURE;

    public static bool IsNetworkUnavailable(Win32Error errorCode)
        => errorCode == Win32Error.ERROR_BAD_NET_NAME || errorCode == Win32Error.ERROR_BAD_NETPATH;

    private static Exception CreateException(Win32Error errorCode, string path)
    {
        var code = unchecked((int)(uint)errorCode);
        var message = new Win32Exception(code).Message;
        if (IsAccessDenied(errorCode))
            return new UnauthorizedAccessException($"Access denied to '{path}': {message}");
        if (IsNetworkUnavailable(errorCode))
            return new IOException($"Network location is unavailable: {message}");
        return new IOException($"Failed to enumerate shares at '{path}': {message}", new Win32Exception(code));
    }
}
