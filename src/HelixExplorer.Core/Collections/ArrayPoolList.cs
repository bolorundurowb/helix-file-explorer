using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace HelixExplorer.Core.Collections;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The struct implements IDisposable; copies intentionally share one idempotent disposable owner.")]
public struct ArrayPoolList<T> : IDisposable
{
    private State? _state;

    public ArrayPoolList() : this(16, null) { }

    public ArrayPoolList(int initialCapacity, ArrayPool<T>? pool = null)
    {
        _state = new State(pool ?? ArrayPool<T>.Shared, Math.Max(initialCapacity, 1));
    }

    public readonly int Count => GetStateOrDefault()?.Count ?? 0;
    public readonly int Capacity => GetStateOrDefault()?.Buffer.Length ?? 0;
    public readonly Span<T> AsSpan()
    {
        var state = GetStateOrDefault();
        return state is null ? Span<T>.Empty : state.Buffer.AsSpan(0, state.Count);
    }

    public readonly ReadOnlySpan<T> AsReadOnlySpan()
    {
        var state = GetStateOrDefault();
        return state is null ? ReadOnlySpan<T>.Empty : state.Buffer.AsSpan(0, state.Count);
    }

    public readonly ref T this[int index]
    {
        get
        {
            var state = GetRequiredState();
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)state.Count);
            return ref state.Buffer[index];
        }
    }

    public void Add(T item)
    {
        var state = EnsureInitialized();
        if (state.Count == state.Buffer.Length)
            state.Grow();
        state.Buffer[state.Count++] = item;
    }

    public void AddRange(ReadOnlySpan<T> items)
    {
        var state = EnsureInitialized();
        state.EnsureCapacity(state.Count + items.Length);
        items.CopyTo(state.Buffer.AsSpan(state.Count));
        state.Count += items.Length;
    }

    public void Clear()
    {
        var state = GetStateOrDefault();
        if (state is null)
            return;

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            Array.Clear(state.Buffer, 0, state.Count);
        state.Count = 0;
    }

    public void Sort(IComparer<T> comparer)
    {
        var state = GetStateOrDefault();
        if (state is null) return;
        Array.Sort(state.Buffer, 0, state.Count, comparer);
    }

    public void Sort(Comparison<T> comparison)
    {
        var state = GetStateOrDefault();
        if (state is null) return;
        Array.Sort(state.Buffer, 0, state.Count, Comparer<T>.Create(comparison));
    }

    public readonly T[] ToArray()
    {
        var state = GetStateOrDefault();
        if (state is null || state.Count == 0)
            return [];

        var result = new T[state.Count];
        Array.Copy(state.Buffer, result, state.Count);
        return result;
    }

    public void Dispose() => _state?.Dispose();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private State EnsureInitialized()
    {
        var state = _state;
        if (state is null)
            return _state = new State(ArrayPool<T>.Shared, 16);

        state.ThrowIfDisposed();
        return state;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly State? GetStateOrDefault()
    {
        _state?.ThrowIfDisposed();
        return _state;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly State GetRequiredState()
    {
        var state = _state ?? throw new ArgumentOutOfRangeException(
            "index", "The list is empty; no element can be indexed.");
        state.ThrowIfDisposed();
        return state;
    }

    private sealed class State(ArrayPool<T> pool, int capacity) : IDisposable
    {
        private T[]? _buffer = pool.Rent(capacity);
        private int _disposed;

        public T[] Buffer
        {
            get
            {
                ThrowIfDisposed();
                return _buffer!;
            }
        }

        public int Count { get; set; }

        public void EnsureCapacity(int min)
        {
            if (Buffer.Length < min)
                Grow(min);
        }

        public void Grow(int minCapacity = 0)
        {
            var buffer = Buffer;
            var newBuffer = pool.Rent(Math.Max(buffer.Length * 2, minCapacity));
            Array.Copy(buffer, newBuffer, Count);
            pool.Return(buffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            _buffer = newBuffer;
        }

        public void ThrowIfDisposed()
            => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, typeof(ArrayPoolList<T>));

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            var buffer = Interlocked.Exchange(ref _buffer, null);
            if (buffer is not null)
                pool.Return(buffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            Count = 0;
        }
    }
}
