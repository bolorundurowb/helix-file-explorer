namespace HelixExplorer.Core.Infrastructure;

/// <summary>
/// Native drag-out service used by the Helix UI when the user drags files/folders from a pane
/// to an external target (Windows Explorer, a browser upload field, another app). Implementations
/// must default to <see cref="DragDropEffects.Copy"/> so browser upload fields treat the drop as an
/// upload rather than a destructive move.
/// </summary>
public interface IExternalFileDragService
{
    /// <summary>
    /// Starts a native drag-out for the already-resolved physical paths and blocks until the drop
    /// completes. Returns the effect the drop target selected, or <see cref="DragDropEffects.None"/>
    /// when the user cancelled or the operation failed.
    /// </summary>
    DragDropEffects DoDragDrop(IReadOnlyList<string> physicalPaths, DragDropEffects allowedEffects);
}

/// <summary>Mirrors <see cref="System.Windows.Forms.DragDropEffects"/> for the cross-platform contract.</summary>
[Flags]
public enum DragDropEffects
{
    None = 0,
    Copy = 1,
    Move = 2,
    Link = 4,
    Scroll = unchecked((int)0x80000000)
}