using HelixExplorer.Core.FileSystem;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.macOS.FileSystem;

public sealed class MacVolumeChangeWatcher(ILogger<MacVolumeChangeWatcher> logger) : IVolumeChangeWatcher
{
    private readonly object _lock = new();
    private bool _started;
    private Timer? _pollTimer;
    private string[] _previousVolumes = [];

    public event EventHandler? VolumesChanged;

    public void Start()
    {
        lock (_lock)
        {
            if (_started)
                return;

            _started = true;
            _previousVolumes = GetCurrentVolumes();

            // Poll /Volumes directory every 2 seconds
            _pollTimer = new Timer(CheckVolumes, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        }
    }

    private void CheckVolumes(object? state)
    {
        try
        {
            var currentVolumes = GetCurrentVolumes();
            if (!currentVolumes.SequenceEqual(_previousVolumes))
            {
                _previousVolumes = currentVolumes;
                VolumesChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Volume check failed");
        }
    }

    private static string[] GetCurrentVolumes()
    {
        var volumes = new List<string> { "/" };
        try
        {
            if (Directory.Exists("/Volumes"))
            {
                volumes.AddRange(Directory.GetDirectories("/Volumes"));
            }
        }
        catch
        {
            // Ignore
        }
        return [.. volumes.OrderBy(v => v)];
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (!_started)
                return;

            _started = false;
            _pollTimer?.Dispose();
            _pollTimer = null;
        }
    }
}