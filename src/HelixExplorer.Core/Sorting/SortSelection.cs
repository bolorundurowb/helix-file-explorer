using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.Sorting;

/// <summary>Pure decision logic for column-header sorting, extracted from the view for testability.</summary>
public static class SortSelection
{
    /// <summary>
    /// Given the current sort state and a newly clicked column, returns the next sort state: clicking
    /// the active column flips its direction; clicking a different column selects it ascending.
    /// </summary>
    public static (SortColumn Column, bool Descending) Toggle(
        SortColumn currentColumn,
        bool currentDescending,
        SortColumn clickedColumn)
    {
        if (currentColumn == clickedColumn)
            return (clickedColumn, !currentDescending);

        return (clickedColumn, false);
    }
}
