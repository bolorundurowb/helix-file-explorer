namespace HelixExplorer.Core.Settings;

public sealed record TabTintOption(string Name, string Hex);

/// <summary>
/// Windows Terminal's tab colour grid, in row-major order. A distinct type from
/// <see cref="FolderColorOption"/> so folder and sidebar menus cannot pick this list up.
/// Names are for screen readers only; the menu shows the swatch.
/// </summary>
public static class TabTintPalette
{
    public static IReadOnlyList<TabTintOption> All { get; } =
    [
        new("Crimson", "#DC143C"),
        new("Steel blue", "#4682B4"),
        new("Medium sea green", "#3CB371"),
        new("Dark orange", "#FF8C00"),
        new("Medium violet red", "#C71585"),
        new("Dodger blue", "#1E90FF"),
        new("Lime green", "#32CD32"),
        new("Yellow", "#FFFF00"),
        new("Blue violet", "#8A2BE2"),
        new("Slate blue", "#6A5ACD"),
        new("Lime", "#00FF00"),
        new("Tan", "#D2B48C"),
        new("Magenta", "#FF00FF"),
        new("Cyan", "#00FFFF"),
        new("Sky blue", "#87CEEB"),
        new("Dark gray", "#A9A9A9"),
    ];
}
