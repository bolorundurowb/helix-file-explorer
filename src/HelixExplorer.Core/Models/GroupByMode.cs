namespace HelixExplorer.Core.Models;

/// <summary>
/// Explorer-style grouping applied to Grid view only. Group order is fixed by
/// <see cref="FileGroupBucket.Order"/>; items inside a group keep the pane's normal sort.
/// </summary>
public enum GroupByMode
{
    None,
    Name,
    Modified,
    Type,
    Size
}
