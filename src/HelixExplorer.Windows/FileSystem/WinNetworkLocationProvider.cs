using System.ComponentModel;
using System.Runtime.InteropServices;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Windows.FileSystem;

/// <summary>
/// Discovers top-level network computers/servers via Explorer's Network shell folder first, then falls
/// back to the legacy WNet APIs. Shares under a server are enumerated lazily by the file system provider
/// when the user opens the server, so startup does not deep-scan every host.
/// </summary>
public sealed class WinNetworkLocationProvider(
    IShellFolderEnumerator shell,
    ILogger<WinNetworkLocationProvider> logger) : INetworkLocationProvider
{
    private const int ResourceConnected = 0x00000001;
    private const int ResourceGlobalNet = 0x00000002;
    private const int ResourceRemembered = 0x00000003;
    private const int ResourceTypeDisk = 0x00000001;
    private const int ResourceUsageContainer = 0x00000002;

    private const int ErrorNoMoreItems = 259;
    private const int ErrorMoreData = 234;

    private const int DisplayTypeDomain = 0x00000001;
    private const int DisplayTypeServer = 0x00000002;
    private const int DisplayTypeNetwork = 0x00000006;
    private const int DisplayTypeRoot = 0x00000007;

    /// <summary>Domains → servers only. Deeper (server → shares) enumeration is deferred to navigation.</summary>
    private const int MaxContainerDepth = 2;

    /// <summary>Overall discovery budget so one offline provider cannot stall the sidebar.</summary>
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(10);

    public async ValueTask<NetworkDiscoveryResult> GetNetworkLocationsAsync(
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DiscoveryTimeout);
        var ct = timeoutCts.Token;

        try
        {
            var shellResult = await EnumerateShellNetworkAsync(ct).ConfigureAwait(false);
            if (shellResult.Status == NetworkDiscoveryStatus.Discovered)
                return shellResult;

            var wnetResult = await Task.Run(() => EnumerateWNetNetworkLocations(ct), ct).ConfigureAwait(false);
            if (wnetResult.Status == NetworkDiscoveryStatus.Discovered)
                return wnetResult;

            return shellResult.Status == NetworkDiscoveryStatus.DiscoveryFailed
                   || wnetResult.Status == NetworkDiscoveryStatus.DiscoveryFailed
                ? NetworkDiscoveryResult.Failed()
                : NetworkDiscoveryResult.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelled by the caller (e.g. window closing) — propagate so the caller can ignore it.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Our own discovery timeout fired. Surface as a failure so the sidebar can say so.
            logger.LogWarning("Network discovery timed out after {Seconds}s", DiscoveryTimeout.TotalSeconds);
            return NetworkDiscoveryResult.Failed();
        }
    }

    private async ValueTask<NetworkDiscoveryResult> EnumerateShellNetworkAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entries = await shell.EnumerateAsync(ShellPath.Network, cancellationToken).ConfigureAwait(false);
            var locations = new List<NetworkLocationInfo>(entries.Count);
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var location = MapShellNetworkEntry(entry);
                if (location is not null)
                    locations.Add(location);
            }

            return NetworkDiscoveryResult.From(NetworkPath.Deduplicate(locations));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Shell Network folder discovery failed");
            return NetworkDiscoveryResult.Failed();
        }
    }

    private static NetworkLocationInfo? MapShellNetworkEntry(FileSystemEntry entry)
    {
        var path = NetworkPath.IsUnc(entry.FullPath)
            ? NetworkPath.Normalize(entry.FullPath)
            : null;

        if (string.IsNullOrWhiteSpace(path))
        {
            var candidate = entry.Name.Trim();
            if (string.IsNullOrWhiteSpace(candidate)
                || candidate.Contains(':', StringComparison.Ordinal)
                || candidate.Contains('\\', StringComparison.Ordinal)
                || candidate.Contains('/', StringComparison.Ordinal))
            {
                return null;
            }

            path = NetworkPath.ForServer(candidate);
        }

        var display = !string.IsNullOrWhiteSpace(entry.Name)
            ? entry.Name
            : NetworkPath.GetServer(path) ?? path;

        return new NetworkLocationInfo(path, display);
    }

    private NetworkDiscoveryResult EnumerateWNetNetworkLocations(CancellationToken cancellationToken)
    {
        var results = new List<NetworkLocationInfo>();
        var openFailed = false;

        // Fallback: enumerate the global network (domains/workgroups → servers).
        openFailed |= !Enumerate(ResourceGlobalNet, null, results, depth: 0, cancellationToken);

        // Fallback: include mapped drives and remembered/persisted UNC connections that Windows already
        // knows about, so users still see their network shares when live discovery is unavailable.
        AddKnownConnections(ResourceConnected, results, cancellationToken);
        AddKnownConnections(ResourceRemembered, results, cancellationToken);

        var unique = NetworkPath.Deduplicate(results);

        if (unique.Count > 0)
            return NetworkDiscoveryResult.From(unique);

        // No locations. If the primary enumeration could not even open, treat it as a failure so the UI
        // can distinguish "nothing shared" from "discovery unavailable".
        return openFailed ? NetworkDiscoveryResult.Failed() : NetworkDiscoveryResult.Empty;
    }

    /// <summary>Returns false when the enumeration could not be opened at this level.</summary>
    private bool Enumerate(
        int scope,
        NetResource? parent,
        List<NetworkLocationInfo> results,
        int depth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var openResult = WNetOpenEnum(scope, ResourceTypeDisk, 0, parent, out var handle);
        if (openResult != 0)
        {
            logger.LogWarning(
                "WNetOpenEnum failed ({Error}) for parent '{Parent}' at depth {Depth}: {Message}",
                openResult,
                parent?.RemoteName ?? "<root>",
                depth,
                new Win32Exception(openResult).Message);
            return false;
        }

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
                        // Buffer too small for even a single entry: grow and retry this iteration.
                        Marshal.FreeHGlobal(buffer);
                        bufferSize = NetworkEnumBuffer.Grow(attemptedSize, bufferSize);
                        buffer = Marshal.AllocHGlobal(bufferSize);
                        count = -1;
                        result = WNetEnumResource(handle, ref count, buffer, ref bufferSize);
                    }

                    if (result == ErrorNoMoreItems)
                        break;

                    if (result != 0)
                    {
                        logger.LogWarning(
                            "WNetEnumResource failed ({Error}) for parent '{Parent}': {Message}",
                            result,
                            parent?.RemoteName ?? "<root>",
                            new Win32Exception(result).Message);
                        break;
                    }

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
                                NetworkPath.Normalize(remoteName),
                                GetDisplayName(remoteName, resource.Comment),
                                resource.Comment));
                        }

                        // Recurse into containers (domain → servers), but stop before deep-scanning every
                        // server's shares at startup.
                        if ((resource.Usage & ResourceUsageContainer) == ResourceUsageContainer
                            && depth + 1 < MaxContainerDepth)
                        {
                            Enumerate(scope, resource, results, depth + 1, cancellationToken);
                        }
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

        return true;
    }

    /// <summary>Enumerates mapped/remembered network connections and adds their UNC roots.</summary>
    private void AddKnownConnections(int scope, List<NetworkLocationInfo> results, CancellationToken cancellationToken)
    {
        if (WNetOpenEnum(scope, ResourceTypeDisk, 0, null, out var handle) != 0)
            return;

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

                    if (result == ErrorNoMoreItems || result != 0)
                        break;

                    var itemSize = Marshal.SizeOf<NetResource>();
                    for (var i = 0; i < count; i++)
                    {
                        var resource = Marshal.PtrToStructure<NetResource>(buffer + i * itemSize);
                        var remoteName = resource?.RemoteName;
                        if (string.IsNullOrWhiteSpace(remoteName) || !NetworkPath.IsUnc(remoteName))
                            continue;

                        results.Add(new NetworkLocationInfo(
                            NetworkPath.Normalize(remoteName),
                            GetDisplayName(remoteName, resource!.Comment),
                            resource.Comment));
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

/// <summary>Pure buffer-sizing logic for WNet enumeration, extracted so it can be unit tested.</summary>
public static class NetworkEnumBuffer
{
    /// <summary>Initial enumeration buffer size (16 KiB).</summary>
    public const int InitialSize = 16 * 1024;

    /// <summary>Upper bound so a misbehaving provider cannot request unbounded memory (1 MiB).</summary>
    public const int MaxSize = 1024 * 1024;

    /// <summary>Doubles the buffer on <c>ERROR_MORE_DATA</c>, capped at <see cref="MaxSize"/>.</summary>
    public static int Grow(int currentSize)
        => Grow(currentSize, requestedSize: 0);

    /// <summary>
    /// Grows the buffer, honoring the size Windows requested when it is larger than a simple doubling.
    /// </summary>
    public static int Grow(int currentSize, int requestedSize)
    {
        if (currentSize <= 0)
            return InitialSize;

        var next = Math.Max((long)currentSize * 2, requestedSize);
        return next >= MaxSize ? MaxSize : (int)next;
    }
}
