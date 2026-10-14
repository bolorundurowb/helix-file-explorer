using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Core.FileSystem;

/// <summary>
/// Chooses copy, move, or link for an in-app file drop.
/// </summary>
public static class DropEffectPolicy
{
    public static DragDropEffects Resolve(string? source, string? destination, bool ctrl, bool shift, bool alt)
    {
        // Alt asks for a shortcut, which is neither a copy nor a move, so it wins over the other modifiers.
        if (alt)
            return DragDropEffects.Link;

        // Ctrl and Shift disagree when both are held. Ctrl is applied first so that combination stays a copy.
        if (ctrl)
            return DragDropEffects.Copy;

        if (shift)
            return DragDropEffects.Move;

        // A UNC path and a drive letter do not share a root, so the default there is copy.
        return PathUtilities.IsSameVolume(source ?? string.Empty, destination ?? string.Empty)
            ? DragDropEffects.Move
            : DragDropEffects.Copy;
    }
}
