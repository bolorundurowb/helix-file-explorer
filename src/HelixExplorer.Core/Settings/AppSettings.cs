using HelixExplorer.Core.Models;
using HelixExplorer.Core.Theming;

namespace HelixExplorer.Core.Settings;

public sealed class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public UiFontFamily UiFont { get; set; } = UiFontFamily.System;
    public double SidebarWidth { get; set; } = 200;
    public SizeDisplayMode SizeDisplay { get; set; } = SizeDisplayMode.Binary;
    public bool ShowHiddenFiles { get; set; }
    public bool ShowFileExtensions { get; set; } = true;
    public LayoutMode DefaultViewMode { get; set; } = LayoutMode.Details;
    public double DefaultThumbnailSize { get; set; } = 72;
    public bool DefaultDualPane { get; set; }
    public PaneSplitOrientation DefaultSplitOrientation { get; set; } = PaneSplitOrientation.Vertical;
    public uint? AccentColorArgb { get; set; }
    public List<string> PinnedPaths { get; set; } = [];
    public List<string> UnpinnedPaths { get; set; } = [];
    public Dictionary<string, uint> FolderColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Last normal (non-maximized) window width in device-independent pixels.</summary>
    public double? WindowWidth { get; set; }

    /// <summary>Last normal (non-maximized) window height in device-independent pixels.</summary>
    public double? WindowHeight { get; set; }

    /// <summary>Last normal window X position in screen coordinates.</summary>
    public int? WindowX { get; set; }

    /// <summary>Last normal window Y position in screen coordinates.</summary>
    public int? WindowY { get; set; }

    /// <summary>Whether the main window was maximized when last closed.</summary>
    public bool WindowMaximized { get; set; }
}

public enum SizeDisplayMode
{
    Binary,
    Decimal
}
