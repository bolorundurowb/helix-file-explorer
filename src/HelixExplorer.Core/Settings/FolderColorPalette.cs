namespace HelixExplorer.Core.Settings;

public sealed record FolderColorOption(string Name, string Hex);

/// <summary>
/// The single colour list offered for folders, sidebar items, and tabs, so the three menus cannot drift apart.
/// </summary>
public static class FolderColorPalette
{
    public static IReadOnlyList<FolderColorOption> All { get; } =
    [
        new("Red", "#E81123"),
        new("Orange", "#F7630C"),
        new("Green", "#107C10"),
        new("Blue", "#0078D4"),
        new("Purple", "#8764B8"),
        new("Teal", "#00B294"),
        new("Pink", "#E3008C"),
    ];
}
