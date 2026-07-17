using HelixExplorer.Core.Models;
using HelixExplorer.Core.Sorting;

namespace HelixExplorer.Core.Tests;

public class SortSelectionTests
{
    [Fact]
    public void ClickingDifferentColumn_SelectsItAscending()
    {
        var (column, descending) = SortSelection.Toggle(SortColumn.Name, currentDescending: true, SortColumn.Size);
        column.Must().Be(SortColumn.Size);
        descending.Must().BeFalse();
    }

    [Fact]
    public void ClickingActiveAscendingColumn_FlipsToDescending()
    {
        var (column, descending) = SortSelection.Toggle(SortColumn.Name, currentDescending: false, SortColumn.Name);
        column.Must().Be(SortColumn.Name);
        descending.Must().BeTrue();
    }

    [Fact]
    public void ClickingActiveDescendingColumn_FlipsToAscending()
    {
        var (column, descending) = SortSelection.Toggle(SortColumn.Modified, currentDescending: true, SortColumn.Modified);
        column.Must().Be(SortColumn.Modified);
        descending.Must().BeFalse();
    }
}
