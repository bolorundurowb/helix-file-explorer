using HelixExplorer.Core.Models;
using HelixExplorer.Core.Theming;

namespace HelixExplorer.Core.Settings;

public sealed class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.System;
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
}

public enum SizeDisplayMode
{
    Binary,
    Decimal
}
