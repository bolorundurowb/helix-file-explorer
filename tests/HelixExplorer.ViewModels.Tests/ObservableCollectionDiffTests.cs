using System.Collections.ObjectModel;
using HelixExplorer.ViewModels.Pane;
using Xunit;

namespace HelixExplorer.ViewModels.Tests;

public class ObservableCollectionDiffTests
{
    private sealed class Item(string name)
    {
        public string Name { get; } = name;
        public override string ToString() => Name;
    }

    [Fact]
    public void Apply_SameOrder_DoesNotRaiseChanges()
    {
        var a = new Item("a");
        var b = new Item("b");
        var target = new ObservableCollection<Item> { a, b };

        var changes = 0;
        target.CollectionChanged += (_, _) => changes++;

        ObservableCollectionDiff.Apply(target, new[] { a, b });

        Assert.Equal(0, changes);
        Assert.Equal(new[] { a, b }, target);
    }

    [Fact]
    public void Apply_Reorder_PreservesInstances()
    {
        var a = new Item("a");
        var b = new Item("b");
        var c = new Item("c");
        var target = new ObservableCollection<Item> { a, b, c };

        ObservableCollectionDiff.Apply(target, new[] { c, a, b });

        Assert.Same(c, target[0]);
        Assert.Same(a, target[1]);
        Assert.Same(b, target[2]);
    }

    [Fact]
    public void Apply_RemovesMissing_AndKeepsSurvivors()
    {
        var a = new Item("a");
        var b = new Item("b");
        var c = new Item("c");
        var target = new ObservableCollection<Item> { a, b, c };

        ObservableCollectionDiff.Apply(target, new[] { a, c });

        Assert.Equal(new[] { a, c }, target);
    }

    [Fact]
    public void Apply_AddsNewItems_AtCorrectPositions()
    {
        var a = new Item("a");
        var c = new Item("c");
        var target = new ObservableCollection<Item> { a, c };

        var b = new Item("b");
        var d = new Item("d");
        ObservableCollectionDiff.Apply(target, new[] { a, b, c, d });

        Assert.Equal(new[] { a, b, c, d }, target);
    }

    [Fact]
    public void Apply_MixedAddRemoveReorder_ProducesDesiredSequence()
    {
        var a = new Item("a");
        var b = new Item("b");
        var c = new Item("c");
        var target = new ObservableCollection<Item> { a, b, c };

        var d = new Item("d");
        var desired = new[] { d, c, a };
        ObservableCollectionDiff.Apply(target, desired);

        Assert.Equal(desired, target);
    }

    [Fact]
    public void Apply_ReusesSurvivingInstances_ForSelectionPreservation()
    {
        // Simulates a sort: same instances, new order. A selection tracked by instance must survive.
        var a = new Item("a");
        var b = new Item("b");
        var target = new ObservableCollection<Item> { a, b };
        var selected = target[1]; // b

        ObservableCollectionDiff.Apply(target, new[] { b, a });

        Assert.Contains(selected, target);
        Assert.Same(b, target[0]);
    }

    [Fact]
    public void Apply_EmptyDesired_ClearsTarget()
    {
        var target = new ObservableCollection<Item> { new("a"), new("b") };
        ObservableCollectionDiff.Apply(target, Array.Empty<Item>());
        Assert.Empty(target);
    }
}
