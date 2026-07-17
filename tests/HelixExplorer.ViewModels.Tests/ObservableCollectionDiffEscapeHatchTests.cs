using System.Collections.ObjectModel;
using HelixExplorer.ViewModels.Pane;

namespace HelixExplorer.ViewModels.Tests;

public class ObservableCollectionDiffEscapeHatchTests
{
    private sealed class Item(string name)
    {
        public string Name { get; } = name;
    }

    [Fact]
    public void Apply_HighChurn_ReplacesCollection()
    {
        var target = new ObservableCollection<Item>();
        for (var i = 0; i < 20; i++)
            target.Add(new Item($"old-{i}"));

        var desired = new List<Item>();
        for (var i = 0; i < 20; i++)
            desired.Add(new Item($"new-{i}"));

        ObservableCollectionDiff.Apply(target, desired);

        target.Count.Must().Be(20);
        target[0].Name.Must().Be("new-0");
        target[19].Name.Must().Be("new-19");
    }

    [Fact]
    public void Apply_MostlyShared_UsesIncrementalPath()
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
}
