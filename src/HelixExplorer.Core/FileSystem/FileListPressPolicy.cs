namespace HelixExplorer.Core.FileSystem;

/// <summary>
/// Shared press rules for Details/List/Grid so a multi-selection is not collapsed until mouse-up
/// when the press may start a group drag (Explorer-style).
/// </summary>
public static class FileListPressPolicy
{
    public static bool ShouldPreserveGroupOnPress(
        int selectedCount,
        bool pressedIsSelected,
        bool modifiersDown)
        => selectedCount > 1 && pressedIsSelected && !modifiersDown;
}
