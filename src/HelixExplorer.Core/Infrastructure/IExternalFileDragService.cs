namespace HelixExplorer.Core.Infrastructure;

/// <summary>
/// Native drag-out to external targets. Implementations must default to
/// <see cref="DragDropEffects.Copy"/> so browser upload fields treat the drop as an upload
/// rather than a destructive move.
/// </summary>
public interface IExternalFileDragService
{
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
