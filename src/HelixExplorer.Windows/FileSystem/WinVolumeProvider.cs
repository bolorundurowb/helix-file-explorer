using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Windows.FileSystem;

public sealed class WinVolumeProvider(ILogger<WinVolumeProvider> logger) : IVolumeProvider
{
    private readonly object _gate = new();
    private IReadOnlyList<VolumeInfo>? _snapshot;

    public IReadOnlyList<VolumeInfo> GetVolumes()
    {
        lock (_gate)
        {
            _snapshot ??= ReadVolumes();
            return _snapshot;
        }
    }

    public async ValueTask<IReadOnlyList<VolumeInfo>> GetVolumesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var volumes = ReadVolumes();
            lock (_gate)
                _snapshot = volumes;
            return volumes;
        }, cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<VolumeInfo> ReadVolumes()
    {
        var drives = DriveInfo.GetDrives();
        var result = new List<VolumeInfo>(drives.Length);

        foreach (var drive in drives)
        {
            string label;
            string display;
            var ready = false;
            long total = 0;
            long free = 0;
            try
            {
                ready = drive.IsReady;
                label = ready ? drive.VolumeLabel : string.Empty;
                display = ready && !string.IsNullOrWhiteSpace(label)
                    ? $"{label} ({drive.Name.TrimEnd('\\')})"
                    : drive.Name.TrimEnd('\\');
                if (ready)
                {
                    total = drive.TotalSize;
                    free = drive.AvailableFreeSpace;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read drive info for '{Drive}'", drive.Name);
                label = string.Empty;
                display = drive.Name.TrimEnd('\\');
            }

            result.Add(new VolumeInfo(
                RootPath: drive.Name,
                Label: label,
                DisplayName: display,
                DriveType: drive.DriveType,
                IsReady: ready,
                TotalBytes: total,
                FreeBytes: free));
        }

        return result;
    }
}
