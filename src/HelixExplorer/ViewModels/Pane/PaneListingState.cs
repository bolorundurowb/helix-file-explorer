using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Models;

namespace HelixExplorer.ViewModels.Pane;

/// <summary>
/// Listing arrays and git snapshot owned separately from pane chrome so refresh can depend on a
/// smaller host surface over time.
/// </summary>
public sealed class PaneListingState
{
    public IReadOnlyList<FileSystemEntry> AllEntries { get; set; } = [];

    public IReadOnlyList<FileSystemEntry> DirectoryEntries { get; set; } = [];

    public GitStatusSnapshot GitSnapshot { get; set; } = GitStatusSnapshot.Empty;

    public void Clear()
    {
        AllEntries = [];
        DirectoryEntries = [];
        GitSnapshot = GitStatusSnapshot.Empty;
    }
}
