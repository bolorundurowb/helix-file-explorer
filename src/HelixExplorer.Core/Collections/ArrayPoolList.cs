using System.Buffers;
using System.Runtime.CompilerServices;

namespace HelixExplorer.Core.Collections;

public struct ArrayPoolList<T> : IDisposable
{
    private T[] _buffer;
    private int _count;
    private ArrayPool<T> _pool;

    public ArrayPoolList() : this(16, null) { }

    public ArrayPoolList(int initialCapacity, ArrayPool<T>? pool = null)
    {
        _pool = pool ?? ArrayPool<T>.Shared;
        _buffer = _pool.Rent(Math.Max(initialCapacity, 1));
        _count = 0;
    }

    public readonly int Count => _count;
    public readonly int Capacity => _buffer?.Length ?? 0;
    public readonly Span<T> AsSpan() => _buffer is null ? Span<T>.Empty : _buffer.AsSpan(0, _count);
    public readonly ReadOnlySpan<T> AsReadOnlySpan() => _buffer is null ? ReadOnlySpan<T>.Empty : _buffer.AsSpan(0, _count);

    public readonly ref T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
                throw new IndexOutOfRangeException();
            return ref _buffer[index];
        }
    }

    public void Add(T item)
    {
        EnsureInitialized();
        if (_count == _buffer.Length)
            Grow();
        _buffer[_count++] = item;
    }

    public void AddRange(ReadOnlySpan<T> items)
    {
        EnsureInitialized();
        EnsureCapacity(_count + items.Length);
        items.CopyTo(_buffer.AsSpan(_count));
        _count += items.Length;
    }

    public void Clear()
    {
        if (_buffer is null)
        {
            _count = 0;
            return;
        }

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            Array.Clear(_buffer, 0, _count);
        _count = 0;
    }

    public void Sort(IComparer<T> comparer)
    {
        Array.Sort(_buffer, 0, _count, comparer);
    }

    public void Sort(Comparison<T> comparison)
    {
        Array.Sort(_buffer, 0, _count, Comparer<T>.Create(comparison));
    }

    public readonly T[] ToArray()
    {
        if (_buffer is null || _count == 0)
            return [];

        var result = new T[_count];
        Array.Copy(_buffer, result, _count);
        return result;
    }

    public void Dispose()
    {
        // Idempotent: atomically detach the buffer so a second Dispose (or a Dispose racing with
        // another) cannot return the same array to the pool twice, which corrupts the pool.
        var buffer = Interlocked.Exchange(ref _buffer, null!);
        if (buffer is not null)
        {
            _pool.Return(buffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            _count = 0;
            _pool = null!;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureInitialized()
    {
        if (_buffer is null)
        {
            _pool = ArrayPool<T>.Shared;
            _buffer = _pool.Rent(16);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCapacity(int min)
    {
        if (_buffer.Length >= min)
            return;
        Grow(min);
    }

    private void Grow(int minCapacity = 0)
    {
        var newCapacity = Math.Max(_buffer.Length * 2, minCapacity);
        var newBuffer = _pool.Rent(newCapacity);
        Array.Copy(_buffer, newBuffer, _count);
        _pool.Return(_buffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        _buffer = newBuffer;
    }
}
