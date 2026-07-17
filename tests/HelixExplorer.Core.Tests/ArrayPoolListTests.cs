using HelixExplorer.Core.Collections;

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
        list.Count.Must().Be(3);
    }

    [Fact]
    public void Indexer_ReturnsCorrectValue()
    {
        using var list = new ArrayPoolList<string>();
        list.Add("hello");
        list.Add("world");
        list[0].Must().Be("hello");
        list[1].Must().Be("world");
    }

    [Fact]
    public void Sort_OrdersCorrectly()
    {
        using var list = new ArrayPoolList<int>();
        list.Add(3);
        list.Add(1);
        list.Add(2);
        list.Sort(Comparer<int>.Default);
        list[0].Must().Be(1);
        list[1].Must().Be(2);
        list[2].Must().Be(3);
    }

    [Fact]
    public void Clear_ResetsCount()
    {
        using var list = new ArrayPoolList<int>();
        list.Add(1);
        list.Add(2);
        list.Clear();
        list.Count.Must().Be(0);
    }

    [Fact]
    public void ToArray_ReturnsCopyOfElements()
    {
        using var list = new ArrayPoolList<int>();
        list.Add(10);
        list.Add(20);
        var array = list.ToArray();
        array.Must().BeSequenceEqual([10, 20]);
    }

    [Fact]
    public void Grow_ExpandsCapacity()
    {
        using var list = new ArrayPoolList<int>(initialCapacity: 2);
        for (int i = 0; i < 100; i++)
            list.Add(i);
        list.Count.Must().Be(100);
        list[99].Must().Be(99);
    }

    [Fact]
    public void AddRange_AppendsAllItems()
    {
        using var list = new ArrayPoolList<int>();
        list.Add(1);
        list.AddRange([2, 3, 4]);
        list.Count.Must().Be(4);
        list[3].Must().Be(4);
    }

    [Fact]
    public void AsSpan_ReturnsCorrectSlice()
    {
        using var list = new ArrayPoolList<int>();
        list.Add(5);
        list.Add(10);
        list.Add(15);
        var span = list.AsSpan();
        span.Length.Must().Be(3);
        span[1].Must().Be(10);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var list = new ArrayPoolList<int>();
        list.Add(1);
        list.Add(2);

        list.Dispose();
        Action act = () => list.Dispose();
        act.NotThrow();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotReturnBufferToPoolTwice()
    {
        // Double-Dispose must not return the buffer twice; that corrupts the pool so later
        // rentals can share the same array.
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

        a.ToArray().Must().AllSatisfy(v => v == 1);
        b.ToArray().Must().AllSatisfy(v => v == 2);
    }

    [Fact]
    public void Clear_OnDefaultInitializedStruct_DoesNotThrow()
    {
        var list = default(ArrayPoolList<int>);

        Action act = () => list.Clear();
        act.NotThrow();

        list.Count.Must().Be(0);
    }

    [Fact]
    public void ToArray_OnDefaultInitializedStruct_ReturnsEmpty()
    {
        var list = default(ArrayPoolList<int>);

        var result = list.ToArray();

        result.Must().BeEmpty();
    }

    [Fact]
    public void Clear_ThenReuse_WorksAfterDefaultInitialization()
    {
        var list = default(ArrayPoolList<int>);
        list.Clear();
        list.Add(42);

        list.Count.Must().Be(1);
        list[0].Must().Be(42);
        list.Dispose();
    }
}
