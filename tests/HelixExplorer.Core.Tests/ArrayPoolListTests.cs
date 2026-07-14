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

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var list = new ArrayPoolList<int>();
        list.Add(1);
        list.Add(2);

        list.Dispose();
        var ex = Record.Exception(() => list.Dispose());

        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotReturnBufferToPoolTwice()
    {
        // Rent, populate and dispose twice. If the second Dispose returned the (already returned)
        // buffer again, subsequent independent rentals could hand out the same array, corrupting
        // the pool. Renting fresh lists afterwards must yield distinct, isolated buffers.
        var list = new ArrayPoolList<int>(initialCapacity: 32);
        for (var i = 0; i < 32; i++)
            list.Add(i);
        list.Dispose();
        list.Dispose();

        using var a = new ArrayPoolList<int>(initialCapacity: 32);
        using var b = new ArrayPoolList<int>(initialCapacity: 32);
        for (var i = 0; i < 32; i++)
        {
            a.Add(1);
            b.Add(2);
        }

        Assert.All(a.ToArray(), v => Assert.Equal(1, v));
        Assert.All(b.ToArray(), v => Assert.Equal(2, v));
    }

    [Fact]
    public void Clear_OnDefaultInitializedStruct_DoesNotThrow()
    {
        var list = default(ArrayPoolList<int>);

        var ex = Record.Exception(() => list.Clear());

        Assert.Null(ex);
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void ToArray_OnDefaultInitializedStruct_ReturnsEmpty()
    {
        var list = default(ArrayPoolList<int>);

        var result = list.ToArray();

        Assert.Empty(result);
    }

    [Fact]
    public void Clear_ThenReuse_WorksAfterDefaultInitialization()
    {
        var list = default(ArrayPoolList<int>);
        list.Clear();
        list.Add(42);

        Assert.Equal(1, list.Count);
        Assert.Equal(42, list[0]);
        list.Dispose();
    }
}
