using System.Collections.ObjectModel;
using HelixExplorer.ViewModels.Pane;

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

        changes.Must().Be(0);
        target.Must().BeSequenceEqual(new[] { a, b });
    }

    [Fact]
    public void Apply_Reorder_PreservesInstances()
    {
        var a = new Item("a");
        var b = new Item("b");
        var c = new Item("c");
        var target = new ObservableCollection<Item> { a, b, c };

        ObservableCollectionDiff.Apply(target, new[] { c, a, b });

        c.Must().Be(target[0]);
        a.Must().Be(target[1]);
        b.Must().Be(target[2]);
    }

    [Fact]
    public void Apply_RemovesMissing_AndKeepsSurvivors()
    {
        var a = new Item("a");
        var b = new Item("b");
        var c = new Item("c");
        var target = new ObservableCollection<Item> { a, b, c };

        ObservableCollectionDiff.Apply(target, new[] { a, c });

        target.Must().BeSequenceEqual(new[] { a, c });
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

        target.Must().BeSequenceEqual(new[] { a, b, c, d });
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

        target.Must().BeSequenceEqual(desired);
    }

    [Fact]
    public void Apply_ReusesSurvivingInstances_ForSelectionPreservation()
    {
        var a = new Item("a");
        var b = new Item("b");
        var target = new ObservableCollection<Item> { a, b };
        var selected = target[1];

        ObservableCollectionDiff.Apply(target, new[] { b, a });

        target.Must().Contain(selected);
        b.Must().Be(target[0]);
    }

    [Fact]
    public void Apply_EmptyDesired_ClearsTarget()
    {
        var target = new ObservableCollection<Item> { new("a"), new("b") };
        ObservableCollectionDiff.Apply(target, Array.Empty<Item>());
        target.Must().BeEmpty();
    }
}
