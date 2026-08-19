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

    /// <summary>
    /// New installs default to <see cref="DirectorySortMode.MixedWithFiles"/>;
    /// existing users can switch back to folders-first.
    /// </summary>
    public DirectorySortMode DirectorySort { get; set; } = DirectorySortMode.MixedWithFiles;
    public LayoutMode DefaultViewMode { get; set; } = LayoutMode.Details;
    public double DefaultThumbnailSize { get; set; } = 72;
    public bool DefaultDualPane { get; set; }
    public PaneSplitOrientation DefaultSplitOrientation { get; set; } = PaneSplitOrientation.Vertical;

    /// <summary>
    /// Whether "Open in new tab" moves the user to the new tab. False opens it in the background and
    /// keeps them on the current tab. Explicit new tabs (Ctrl+T, Settings) always take focus.
    /// </summary>
    public bool SwitchToNewTabOnOpen { get; set; } = true;

    public uint? AccentColorArgb { get; set; }
    public List<string> PinnedPaths { get; set; } = [];
    public List<string> UnpinnedPaths { get; set; } = [];

    // Per-folder view preferences and folder colors have moved to the SQLite app database
    // (helix.db). A one-time migration in SqliteAppDatabase.Initialize() copies legacy JSON
    // entries before this class ever loads.

    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public bool WindowMaximized { get; set; }

    /// <summary>
    /// Avalonia <see cref="Avalonia.Input.KeyGesture"/> syntax for "Open in Terminal".
    /// Empty or invalid values fall back to the default (<c>Ctrl+OemTilde</c>) at load time.
    /// </summary>
    public string OpenInTerminalGesture { get; set; } = "Ctrl+OemTilde";
    public bool AutoCheckForUpdates { get; set; } = true;
}

public enum SizeDisplayMode
{
    Binary,
    Decimal
}
