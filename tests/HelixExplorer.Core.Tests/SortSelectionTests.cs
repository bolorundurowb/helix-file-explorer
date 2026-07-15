using HelixExplorer.Core.Models;
using HelixExplorer.Core.Sorting;
using Xunit;

namespace HelixExplorer.Core.Tests;

public class SortSelectionTests
{
    [Fact]
    public void ClickingDifferentColumn_SelectsItAscending()
    {
        var (column, descending) = SortSelection.Toggle(SortColumn.Name, currentDescending: true, SortColumn.Size);
        Assert.Equal(SortColumn.Size, column);
        Assert.False(descending);
    }

    [Fact]
    public void ClickingActiveAscendingColumn_FlipsToDescending()
    {
        var (column, descending) = SortSelection.Toggle(SortColumn.Name, currentDescending: false, SortColumn.Name);
        Assert.Equal(SortColumn.Name, column);
        Assert.True(descending);
    }

    [Fact]
    public void ClickingActiveDescendingColumn_FlipsToAscending()
    {
        var (column, descending) = SortSelection.Toggle(SortColumn.Modified, currentDescending: true, SortColumn.Modified);
        Assert.Equal(SortColumn.Modified, column);
        Assert.False(descending);
    }
}
