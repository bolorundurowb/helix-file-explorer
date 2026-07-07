namespace HelixExplorer.Services;

/// <summary>In-memory user settings. Persist to JSON/disk in a real app.</summary>
public sealed class SettingsService : ISettingsService
{
    private SizeDisplayMode _fileSizeDisplayMode = SizeDisplayMode.Binary;

    public SizeDisplayMode FileSizeDisplayMode
    {
        get => _fileSizeDisplayMode;
        set
        {
            if (_fileSizeDisplayMode != value)
            {
                _fileSizeDisplayMode = value;
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? SettingsChanged;
}