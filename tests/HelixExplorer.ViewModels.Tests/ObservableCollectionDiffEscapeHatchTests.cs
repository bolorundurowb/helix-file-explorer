using System.Collections.ObjectModel;
using HelixExplorer.ViewModels.Pane;
using Xunit;

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

        Assert.Equal(20, target.Count);
        Assert.Equal("new-0", target[0].Name);
        Assert.Equal("new-19", target[19].Name);
    }

    [Fact]
    public void Apply_MostlyShared_UsesIncrementalPath()
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
}
