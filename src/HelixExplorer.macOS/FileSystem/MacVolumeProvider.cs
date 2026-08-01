using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.macOS.FileSystem;

public sealed class MacVolumeProvider(ILogger<MacVolumeProvider> logger) : IVolumeProvider
{
    public IReadOnlyList<VolumeInfo> GetVolumes()
    {
        var volumes = new List<VolumeInfo>();

        try
        {
            var root = GetDriveInfo("/");
            if (root is not null)
                volumes.Add(root);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to get root volume");
        }

        try
        {
            if (Directory.Exists("/Volumes"))
            {
                foreach (var dir in Directory.GetDirectories("/Volumes"))
                {
                    try
                    {
                        var info = GetDriveInfo(dir);
                        if (info is not null)
                            volumes.Add(info);
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Failed to get volume info for '{Path}'", dir);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to enumerate /Volumes");
        }

        return volumes;
    }

    private static VolumeInfo? GetDriveInfo(string path)
    {
        try
        {
            var drive = new DriveInfo(path);
            if (!drive.IsReady)
                return null;

            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? Path.GetFileName(path) : drive.VolumeLabel;
            var driveType = MapDriveType(drive.DriveType, path);
            return new VolumeInfo(
                RootPath: path,
                Label: label,
                DisplayName: label,
                DriveType: driveType,
                IsReady: true,
                TotalBytes: (long)drive.TotalSize,
                FreeBytes: (long)drive.AvailableFreeSpace);
        }
        catch
        {
            return null;
        }
    }

    private static DriveType MapDriveType(DriveType dotNetType, string path)
    {
        if (dotNetType == DriveType.Network)
            return DriveType.Network;
        if (dotNetType == DriveType.CDRom)
            return DriveType.CDRom;
        if (dotNetType == DriveType.Removable)
            return DriveType.Removable;
        if (path == "/")
            return DriveType.Fixed;
        return dotNetType;
    }
}