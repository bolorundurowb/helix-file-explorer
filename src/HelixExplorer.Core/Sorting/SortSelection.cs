using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.Sorting;

/// <summary>Extracted for unit tests without Avalonia.</summary>
public static class SortSelection
{
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
