namespace HelixExplorer.Core.Theming;

/// <summary>
/// System colours of the active Windows contrast theme, as opaque ARGB.
/// </summary>
public readonly record struct HighContrastPalette(
    uint Window,
    uint WindowText,
    uint Highlight,
    uint HighlightText,
    uint GrayText,
    uint Hotlight)
{
    /// <summary>
    /// Contrast themes only replace the colours the app maps; everything else falls back to the
    /// light or dark dictionary, so the fallback must match the brightness of the window colour.
    /// </summary>
    public bool IsDarkSurface => AccentColorDefaults.ForegroundFor(Window) == 0xFFFFFFFFu;
}
