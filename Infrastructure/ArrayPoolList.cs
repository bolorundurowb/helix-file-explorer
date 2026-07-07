using System.Buffers;
using System.Runtime.CompilerServices;

namespace HelixExplorer.Infrastructure;

/// <summary>
/// A pooling facade so call-sites can do <c>var list = ArrayPoolList&lt;T&gt;.Rent();</c>
/// enumerate it, then <c>list.Dispose()</c> to return the buffers. Internally it
/// keeps a single rented <c>T[]</c> that grows on demand through ArrayPool.
/// </summary>
public sealed class ArrayPoolList<T> : IList<T>, IReadOnlyList<T>, IDisposable
{
    private T[] _buffer;
    private int _count;
    private bool _disposed;
    private readonly bool _returnArray = true;

    private static readonly T[] s_empty = Array.Empty<T>();

    public static ArrayPoolList<T> Rent(int capacity = 64)
    {
        capacity = capacity < 64 ? 64 : capacity;
        var buffer = ArrayPool<T>.Shared.Rent(capacity);
        return new ArrayPoolList<T>(buffer);
    }

    private ArrayPoolList(T[] buffer)
    {
        _buffer = buffer;
        _count = 0;
    }

    public int Count => _count;
    public bool IsReadOnly => false;

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count) ThrowIndexOutOfRange(index);
            return _buffer[index];
        }
        set
        {
            if ((uint)index >= (uint)_count) ThrowIndexOutOfRange(index);
            _buffer[index] = value;
        }
    }

    public Span<T> AsSpan() => _buffer.AsSpan(0, _count);
    public T[] UnsafeUnderlyingArray => _buffer;
    public int Capacity => _buffer.Length;

    public void Add(T item)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_count == _buffer.Length) Grow();
        _buffer[_count++] = item;
    }

    public void AddRange(ReadOnlySpan<T> items)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_count + items.Length > _buffer.Length) GrowTo(_count + items.Length);
        items.CopyTo(_buffer.AsSpan(_count));
        _count += items.Length;
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            Array.Clear(_buffer, 0, _count);
        }
        _count = 0;
    }

    public bool Contains(T item) => IndexOf(item) >= 0;

    public void CopyTo(T[] array, int arrayIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Array.Copy(_buffer, 0, array, arrayIndex, _count);
    }

    /// <summary>Shrinks the list to an IReadOnlyList snapshot the caller owns. The pool list itself is reset.</summary>
    public IReadOnlyList<T> ToReadOnlyAndReset()
    {
        if (_count == 0) return Array.Empty<T>();
        var snapshot = new T[_count];
        Array.Copy(_buffer, snapshot, _count);
        Clear();
        return snapshot;
    }

    public int IndexOf(T item) => Array.IndexOf(_buffer, item, 0, _count);

    public void Insert(int index, T item) => ThrowReadOnlyCollection();

    public bool Remove(T item) => ThrowReadOnlyCollection();

    public void RemoveAt(int index) => ThrowReadOnlyCollection();

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        for (int i = 0; i < _count; i++) yield return _buffer[i];
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        for (int i = 0; i < _count; i++) yield return _buffer[i];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_returnArray)
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                Array.Clear(_buffer, 0, _count);
            }
            ArrayPool<T>.Shared.Return(_buffer);
        }
        _buffer = s_empty;
        _count = 0;
    }

    private void Grow()
    {
        int newCapacity = _buffer.Length == 0 ? 64 : _buffer.Length * 2;
        GrowTo(newCapacity);
    }

    private void GrowTo(int minCapacity)
    {
        int newCapacity = _buffer.Length;
        while (newCapacity < minCapacity) newCapacity *= 2;
        var newBuffer = ArrayPool<T>.Shared.Rent(newCapacity);
        Array.Copy(_buffer, newBuffer, _count);
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            Array.Clear(_buffer, 0, _count);
        }
        ArrayPool<T>.Shared.Return(_buffer);
        _buffer = newBuffer;
    }

    private static bool ThrowReadOnlyCollection() => throw new NotSupportedException();
    private static void ThrowIndexOutOfRange(int i) => throw new ArgumentOutOfRangeException(nameof(i), i, "Index out of range");
}