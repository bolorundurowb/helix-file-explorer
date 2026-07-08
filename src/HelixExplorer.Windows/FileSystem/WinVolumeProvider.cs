using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;

namespace HelixExplorer.Windows.FileSystem;

public sealed class WinVolumeProvider : IVolumeProvider
{
    public IReadOnlyList<VolumeInfo> GetVolumes()
    {
        var drives = DriveInfo.GetDrives();
        var result = new List<VolumeInfo>(drives.Length);

        foreach (var drive in drives)
        {
            string label;
            string display;
            var ready = false;
            try
            {
                ready = drive.IsReady;
                label = ready ? drive.VolumeLabel : string.Empty;
                display = ready && !string.IsNullOrWhiteSpace(label)
                    ? $"{label} ({drive.Name.TrimEnd('\\')})"
                    : drive.Name.TrimEnd('\\');
            }
            catch
            {
                label = string.Empty;
                display = drive.Name.TrimEnd('\\');
            }

            result.Add(new VolumeInfo(
                RootPath: drive.Name,
                Label: label,
                DisplayName: display,
                DriveType: drive.DriveType,
                IsReady: ready));
        }

        return result;
    }
}
