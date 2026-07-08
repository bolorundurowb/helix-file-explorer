using System.ComponentModel;
using System.Runtime.InteropServices;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;

namespace HelixExplorer.Windows.FileSystem;

public sealed class WinNetworkLocationProvider : INetworkLocationProvider
{
    private const int ResourceGlobalNet = 0x00000002;
    private const int ResourceTypeDisk = 0x00000001;
    private const int ResourceUsageContainer = 0x00000002;
    private const int ErrorNoMoreItems = 259;

    private const int DisplayTypeDomain = 0x00000001;
    private const int DisplayTypeServer = 0x00000002;
    private const int DisplayTypeNetwork = 0x00000006;
    private const int DisplayTypeRoot = 0x00000007;

    public async ValueTask<IReadOnlyList<NetworkLocationInfo>> GetNetworkLocationsAsync(
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => EnumerateNetworkLocations(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    private static IReadOnlyList<NetworkLocationInfo> EnumerateNetworkLocations(CancellationToken cancellationToken)
    {
        var results = new List<NetworkLocationInfo>();
        Enumerate(null, results, cancellationToken);

        if (results.Count == 0)
            return results;

        // De-dupe by path (case-insensitive) without LINQ OrderBy allocations in a hot path sense —
        // discovery itself is I/O-bound, but keep the filter explicit.
        results.Sort(static (a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

        var unique = new List<NetworkLocationInfo>(results.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var location in results)
        {
            if (seen.Add(location.Path))
                unique.Add(location);
        }

        return unique;
    }

    private static void Enumerate(NetResource? parent, List<NetworkLocationInfo> results, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var openResult = WNetOpenEnum(ResourceGlobalNet, ResourceTypeDisk, 0, parent, out var handle);
        if (openResult != 0)
            return;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var count = -1;
                var bufferSize = 16 * 1024;
                var buffer = Marshal.AllocHGlobal(bufferSize);
                try
                {
                    var result = WNetEnumResource(handle, ref count, buffer, ref bufferSize);
                    if (result == ErrorNoMoreItems)
                        break;
                    if (result != 0)
                        throw new Win32Exception(result);

                    var itemSize = Marshal.SizeOf<NetResource>();
                    for (var i = 0; i < count; i++)
                    {
                        var resource = Marshal.PtrToStructure<NetResource>(buffer + i * itemSize);
                        if (resource is null)
                            continue;

                        var remoteName = resource.RemoteName;
                        if (!string.IsNullOrWhiteSpace(remoteName) && ShouldInclude(resource))
                        {
                            results.Add(new NetworkLocationInfo(
                                remoteName,
                                GetDisplayName(remoteName, resource.Comment),
                                resource.Comment));
                        }

                        if ((resource.Usage & ResourceUsageContainer) == ResourceUsageContainer)
                            Enumerate(resource, results, cancellationToken);
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
    }

    private static bool ShouldInclude(NetResource resource)
    {
        // Skip low-level provider roots (Terminal Services, Web Client, Plan 9, etc.).
        if (resource.DisplayType is DisplayTypeNetwork or DisplayTypeRoot)
            return false;

        // Sidebar shows workgroups and computers, not every share discovered during enumeration.
        return resource.DisplayType is DisplayTypeDomain or DisplayTypeServer;
    }

    private static string GetDisplayName(string remoteName, string? comment)
    {
        if (!string.IsNullOrWhiteSpace(comment))
            return comment;

        var trimmed = remoteName.TrimEnd('\\');
        var index = trimmed.LastIndexOf('\\');
        return index >= 0 && index < trimmed.Length - 1 ? trimmed[(index + 1)..] : trimmed;
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
