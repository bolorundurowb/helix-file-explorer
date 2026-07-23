using System.ComponentModel;
using System.Runtime.InteropServices;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Sorting;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Windows.FileSystem;

/// <summary>
/// Enumerates SMB disk shares under a <c>\\server</c> root via WNet.
/// </summary>
internal static class WinNetworkShareEnumerator
{
    private const int ResourceGlobalNet = 0x00000002;
    private const int ResourceTypeDisk = 0x00000001;
    private const int DisplayTypeShare = 0x00000003;
    private const int ErrorNoMoreItems = 259;
    private const int ErrorMoreData = 234;
    private const int ErrorAccessDenied = 5;
    private const int ErrorLogonFailure = 1326;
    private const int ErrorBadNetName = 67;
    private const int ErrorBadNetPath = 53;

    public static IReadOnlyList<FileSystemEntry> EnumerateShares(string serverRoot, ILogger logger, CancellationToken cancellationToken)
    {
        var normalized = NetworkPath.Normalize(serverRoot);
        if (!NetworkPath.IsServerRoot(normalized))
            return Array.Empty<FileSystemEntry>();

        var parent = new NetResource
        {
            Scope = ResourceGlobalNet,
            Type = ResourceTypeDisk,
            DisplayType = 0,
            Usage = 0,
            RemoteName = normalized
        };

        var openResult = WNetOpenEnum(ResourceGlobalNet, ResourceTypeDisk, 0, parent, out var handle);
        if (openResult != 0)
            throw CreateException(openResult, normalized);

        var results = new List<FileSystemEntry>();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var count = -1;
                var bufferSize = NetworkEnumBuffer.InitialSize;
                var buffer = Marshal.AllocHGlobal(bufferSize);
                try
                {
                    var attemptedSize = bufferSize;
                    var result = WNetEnumResource(handle, ref count, buffer, ref bufferSize);

                    if (result == ErrorMoreData)
                    {
                        Marshal.FreeHGlobal(buffer);
                        bufferSize = NetworkEnumBuffer.Grow(attemptedSize, bufferSize);
                        buffer = Marshal.AllocHGlobal(bufferSize);
                        count = -1;
                        result = WNetEnumResource(handle, ref count, buffer, ref bufferSize);
                    }

                    if (result == ErrorNoMoreItems)
                        break;

                    if (result != 0)
                        throw CreateException(result, normalized);

                    var itemSize = Marshal.SizeOf<NetResource>();
                    for (var i = 0; i < count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var resource = Marshal.PtrToStructure<NetResource>(buffer + i * itemSize);
                        if (resource is null || resource.DisplayType != DisplayTypeShare)
                            continue;

                        var remoteName = resource.RemoteName;
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
            WNetCloseEnum(handle);
        }

        cancellationToken.ThrowIfCancellationRequested();
        results.Sort(FileSystemEntryComparer.For(SortColumn.Name, descending: false));
        logger.LogDebug("Enumerated {Count} shares under '{Server}'", results.Count, normalized);
        return results;
    }

    public static bool IsAccessDenied(int errorCode)
        => errorCode is ErrorAccessDenied or ErrorLogonFailure;

    public static bool IsNetworkUnavailable(int errorCode)
        => errorCode is ErrorBadNetName or ErrorBadNetPath;

    private static Exception CreateException(int errorCode, string path)
    {
        var message = new Win32Exception(errorCode).Message;
        if (IsAccessDenied(errorCode))
            return new UnauthorizedAccessException($"Access denied to '{path}': {message}");
        if (IsNetworkUnavailable(errorCode))
            return new IOException($"Network location is unavailable: {message}");
        return new IOException($"Failed to enumerate shares at '{path}': {message}", new Win32Exception(errorCode));
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetOpenEnum(int dwScope, int dwType, int dwUsage, NetResource? lpNetResource, out IntPtr lphEnum);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetEnumResource(IntPtr hEnum, ref int lpcCount, IntPtr lpBuffer, ref int lpBufferSize);

    [DllImport("mpr.dll")]
    private static extern int WNetCloseEnum(IntPtr hEnum);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class NetResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }
}
