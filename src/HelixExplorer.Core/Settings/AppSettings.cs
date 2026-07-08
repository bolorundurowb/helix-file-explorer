using HelixExplorer.Core.Theming;

namespace HelixExplorer.Core.Settings;

public sealed class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public bool SidebarOpen { get; set; } = true;
    public double SidebarWidth { get; set; } = 240;
    public SizeDisplayMode SizeDisplay { get; set; } = SizeDisplayMode.Binary;
    public bool ShowHiddenFiles { get; set; }
    public bool ShowFileExtensions { get; set; } = true;
    public Dictionary<string, uint> FolderColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum SizeDisplayMode
{
    Binary,
    Decimal
}
