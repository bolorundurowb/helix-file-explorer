namespace HelixExplorer.Core.FileSystem;

public enum FileOperationKind
{
    Copy,
    Move,
    Delete
}

public sealed record FileOperationProgress(
    int CompletedItems,
    int TotalItems,
    string? CurrentPath,
    FileOperationKind Kind);
