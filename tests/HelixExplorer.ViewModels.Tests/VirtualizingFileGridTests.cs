using HelixExplorer.Controls;

namespace HelixExplorer.ViewModels.Tests;

public sealed class VirtualizingFileGridTests
{
    [Fact]
    public void SameReferences_IdenticalLists_ReturnsTrue()
    {
        var a = new object();
        var b = new object();

        VirtualizingFileGrid.SameReferences([a, b], [a, b]).Must().BeTrue();
    }

    [Fact]
    public void SameReferences_DifferentOrder_ReturnsFalse()
    {
        var a = new object();
        var b = new object();

        VirtualizingFileGrid.SameReferences([a, b], [b, a]).Must().BeFalse();
    }

    [Fact]
    public void SameReferences_DifferentInstances_ReturnsFalse()
    {
        VirtualizingFileGrid.SameReferences([new object()], [new object()]).Must().BeFalse();
    }

    [Fact]
    public void SameReferences_DifferentCount_ReturnsFalse()
    {
        var a = new object();

        VirtualizingFileGrid.SameReferences([a], [a, a]).Must().BeFalse();
    }

    [Fact]
    public void SameReferences_EmptyLists_ReturnsTrue()
    {
        VirtualizingFileGrid.SameReferences([], []).Must().BeTrue();
    }
}
