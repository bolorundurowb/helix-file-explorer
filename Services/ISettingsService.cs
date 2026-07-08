namespace HelixExplorer.Services;

/// <summary>User preferences that affect the UI. Persisted to disk as JSON.</summary>
public interface ISettingsService
{
    SizeDisplayMode FileSizeDisplayMode { get; set; }

    /// <summary>Default thumbnail edge length (px) for Grid view, 32–256.</summary>
    double ThumbnailSize { get; set; }

    /// <summary>Default layout mode for new panes (cast of <c>LayoutMode</c>).</summary>
    int DefaultViewMode { get; set; }

    /// <summary>When true, dual-pane splits vertically (top/bottom) instead of side-by-side.</summary>
    bool DualPaneVertical { get; set; }

    event EventHandler? SettingsChanged;
}
