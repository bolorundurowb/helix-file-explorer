using System.ComponentModel;
using System.Runtime.InteropServices;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static Vanara.PInvoke.Mpr;

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

        var locations = new List<NetworkLocationInfo>();

        try
        {
            locations.AddRange(await TryEnumerateShellNetworkAsync(ct).ConfigureAwait(false));

            if (locations.Count == 0)
            {
                var wnetCall = Task.Run(() => TryEnumerateWNetNetworkLocations(ct), CancellationToken.None);
                locations.AddRange(await NativeCallTimeout
                    .AwaitAsync(wnetCall, DiscoveryTimeout, cancellationToken)
                    .ConfigureAwait(false));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Network discovery timed out after {Seconds}s; returning partial results", DiscoveryTimeout.TotalSeconds);
        }
        catch (TimeoutException)
        {
            logger.LogDebug("Native network discovery timed out after {Seconds}s; returning partial results", DiscoveryTimeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Network discovery failed; returning partial results");
        }

        return NetworkDiscoveryResult.From(NetworkPath.Deduplicate(locations));
    }

    private async Task<IReadOnlyList<NetworkLocationInfo>> TryEnumerateShellNetworkAsync(CancellationToken cancellationToken)
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

            return locations;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Shell Network folder discovery failed");
            return Array.Empty<NetworkLocationInfo>();
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

    private List<NetworkLocationInfo> TryEnumerateWNetNetworkLocations(CancellationToken cancellationToken)
    {
        var results = new List<NetworkLocationInfo>();

        Enumerate(NETRESOURCEScope.RESOURCE_GLOBALNET, null, results, depth: 0, cancellationToken);

        // Mapped/remembered UNC connections so users still see shares when live discovery is unavailable.
        AddKnownConnections(NETRESOURCEScope.RESOURCE_CONNECTED, results, cancellationToken);
        AddKnownConnections(NETRESOURCEScope.RESOURCE_REMEMBERED, results, cancellationToken);

        return results;
    }

    private void Enumerate(
        NETRESOURCEScope scope,
        NETRESOURCE? parent,
        List<NetworkLocationInfo> results,
        int depth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var openResult = WNetOpenEnum(scope, NETRESOURCEType.RESOURCETYPE_DISK, 0, parent!, out var handle);
        if (openResult != Win32Error.ERROR_SUCCESS)
        {
            logger.LogDebug(
                "WNetOpenEnum failed ({Error}) for parent '{Parent}' at depth {Depth}: {Message}",
                openResult,
                parent?.lpRemoteName ?? "<root>",
                depth,
                new Win32Exception(unchecked((int)(uint)openResult)).Message);
            return;
        }

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
                    {
                        logger.LogDebug(
                            "WNetEnumResource failed ({Error}) for parent '{Parent}': {Message}",
                            result,
                            parent?.lpRemoteName ?? "<root>",
                            new Win32Exception(unchecked((int)(uint)result)).Message);
                        break;
                    }

                    var itemSize = Marshal.SizeOf<NETRESOURCE>();
                    for (var i = 0; i < count; i++)
                    {
                        var resource = Marshal.PtrToStructure<NETRESOURCE>(buffer + i * itemSize);
                        if (resource is null)
                            continue;

                        var remoteName = resource.lpRemoteName;
                        if (!string.IsNullOrWhiteSpace(remoteName) && ShouldInclude(resource))
                        {
                            results.Add(new NetworkLocationInfo(
                                NetworkPath.Normalize(remoteName),
                                GetDisplayName(remoteName, resource.lpComment),
                                resource.lpComment));
                        }

                        if ((resource.dwUsage & NETRESOURCEUsage.RESOURCEUSAGE_CONTAINER) != 0
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
            handle.Dispose();
        }
    }

    private void AddKnownConnections(NETRESOURCEScope scope, List<NetworkLocationInfo> results, CancellationToken cancellationToken)
    {
        if (WNetOpenEnum(scope, NETRESOURCEType.RESOURCETYPE_DISK, 0, null!, out var handle) != Win32Error.ERROR_SUCCESS)
            return;

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

                    if (result == Win32Error.ERROR_NO_MORE_ITEMS || result != Win32Error.ERROR_SUCCESS)
                        break;

                    var itemSize = Marshal.SizeOf<NETRESOURCE>();
                    for (var i = 0; i < count; i++)
                    {
                        var resource = Marshal.PtrToStructure<NETRESOURCE>(buffer + i * itemSize);
                        var remoteName = resource?.lpRemoteName;
                        if (string.IsNullOrWhiteSpace(remoteName) || !NetworkPath.IsUnc(remoteName))
                            continue;

                        results.Add(new NetworkLocationInfo(
                            NetworkPath.Normalize(remoteName),
                            GetDisplayName(remoteName, resource!.lpComment),
                            resource.lpComment));
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
    }

    private static bool ShouldInclude(NETRESOURCE resource)
    {
        if (resource.dwDisplayType is NETRESOURCEDisplayType.RESOURCEDISPLAYTYPE_NETWORK
            or NETRESOURCEDisplayType.RESOURCEDISPLAYTYPE_ROOT)
        {
            return false;
        }

        return resource.dwDisplayType is NETRESOURCEDisplayType.RESOURCEDISPLAYTYPE_DOMAIN
            or NETRESOURCEDisplayType.RESOURCEDISPLAYTYPE_SERVER;
    }

    private static string GetDisplayName(string remoteName, string? comment)
    {
        if (!string.IsNullOrWhiteSpace(comment))
            return comment;

        var trimmed = remoteName.TrimEnd('\\');
        var index = trimmed.LastIndexOf('\\');
        return index >= 0 && index < trimmed.Length - 1 ? trimmed[(index + 1)..] : trimmed;
    }
}

/// <summary>Buffer sizing for WNet enumeration (extracted for unit tests).</summary>
public static class NetworkEnumBuffer
{
    public const int InitialSize = 16 * 1024;

    /// <summary>Cap so a misbehaving provider cannot request unbounded memory (1 MiB).</summary>
    public const int MaxSize = 1024 * 1024;

    public static int Grow(int currentSize)
        => Grow(currentSize, requestedSize: 0);

    /// <summary>Honours Windows' requested size on <c>ERROR_MORE_DATA</c> when larger than a simple doubling.</summary>
    public static int Grow(int currentSize, int requestedSize)
    {
        if (currentSize <= 0)
            return InitialSize;

        var next = Math.Max((long)currentSize * 2, requestedSize);
        return next >= MaxSize ? MaxSize : (int)next;
    }
}
