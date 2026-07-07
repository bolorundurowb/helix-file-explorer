namespace HelixExplorer.Services;

/// <summary>User preferences that affect the UI.</summary>
public interface ISettingsService
{
    SizeDisplayMode FileSizeDisplayMode { get; set; }
    event EventHandler? SettingsChanged;
}