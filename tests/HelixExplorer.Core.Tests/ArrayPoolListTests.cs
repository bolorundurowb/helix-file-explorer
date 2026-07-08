using HelixExplorer.Core.Collections;
using Xunit;

namespace HelixExplorer.Core.Tests;

public class ArrayPoolListTests
{
    [Fact]
    public void Add_IncreasesCount()
    {
        using var list = new ArrayPoolList<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void Indexer_ReturnsCorrectValue()
    {
        using var list = new ArrayPoolList<string>();
        list.Add("hello");
        list.Add("world");
        Assert.Equal("hello", list[0]);
        Assert.Equal("world", list[1]);
    }

    [Fact]
    public void Sort_OrdersCorrectly()
    {
        using var list = new ArrayPoolList<int>();
        list.Add(3);
        list.Add(1);
        list.Add(2);
        list.Sort(Comparer<int>.Default);
        Assert.Equal(1, list[0]);
        Assert.Equal(2, list[1]);
        Assert.Equal(3, list[2]);
    }

    [Fact]
    public void Clear_ResetsCount()
    {
        using var list = new ArrayPoolList<int>();
        list.Add(1);
        list.Add(2);
        list.Clear();
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void ToArray_ReturnsCopyOfElements()
    {
        using var list = new ArrayPoolList<int>();
        list.Add(10);
        list.Add(20);
        var array = list.ToArray();
        Assert.Equal([10, 20], array);
    }

    [Fact]
    public void Grow_ExpandsCapacity()
    {
        using var list = new ArrayPoolList<int>(initialCapacity: 2);
        for (int i = 0; i < 100; i++)
            list.Add(i);
        Assert.Equal(100, list.Count);
        Assert.Equal(99, list[99]);
    }

    [Fact]
    public void AddRange_AppendsAllItems()
    {
        using var list = new ArrayPoolList<int>();
        list.Add(1);
        list.AddRange([2, 3, 4]);
        Assert.Equal(4, list.Count);
        Assert.Equal(4, list[3]);
    }

    [Fact]
    public void AsSpan_ReturnsCorrectSlice()
    {
        using var list = new ArrayPoolList<int>();
        list.Add(5);
        list.Add(10);
        list.Add(15);
        var span = list.AsSpan();
        Assert.Equal(3, span.Length);
        Assert.Equal(10, span[1]);
    }
}
