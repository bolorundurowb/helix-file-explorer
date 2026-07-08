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

    public ValueTask<IReadOnlyList<NetworkLocationInfo>> GetNetworkLocationsAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(EnumerateNetworkLocations(cancellationToken));

    private static IReadOnlyList<NetworkLocationInfo> EnumerateNetworkLocations(CancellationToken cancellationToken)
    {
        var results = new List<NetworkLocationInfo>();
        Enumerate(null, results, cancellationToken);

        return results
            .GroupBy(location => location.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(location => location.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
                        var remoteName = resource?.RemoteName;
                        if (!string.IsNullOrWhiteSpace(remoteName))
                        {
                            results.Add(new NetworkLocationInfo(
                                remoteName,
                                GetDisplayName(remoteName, resource?.Comment),
                                resource?.Comment));
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
