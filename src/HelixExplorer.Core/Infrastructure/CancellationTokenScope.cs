namespace HelixExplorer.Core.Infrastructure;

/// <summary>
/// Owns a <see cref="CancellationTokenSource"/> that can be renewed on the fly, cancelling and
/// disposing the previous generation. Used for debounced or rapid asynchronous actions (search, folder
/// browsing, preference persistence) so each new request supersedes the last without leaking sources.
/// Thread-safe. Disposing cancels and releases the current source.
/// </summary>
public sealed class CancellationTokenScope : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _source;
    private bool _disposed;

    /// <summary>Gets the current token, lazily creating the first source if none exists yet.</summary>
    public CancellationToken Token => GetOrCreateSource().Token;

    /// <summary>Cancels the current source (if any) and returns a token for the next generation.</summary>
    public CancellationToken Renew()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        CancellationTokenSource? old;
        CancellationTokenSource created;
        lock (_gate)
        {
            old = _source;
            created = new CancellationTokenSource();
            _source = created;
        }

        CancelAndDispose(old);
        return created.Token;
    }

    /// <summary>Cancels and releases the current source. Safe to call more than once.</summary>
    public void Cancel()
    {
        CancellationTokenSource? old;
        lock (_gate)
        {
            old = _source;
            _source = null;
        }

        CancelAndDispose(old);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Cancel();
    }

    private CancellationTokenSource GetOrCreateSource()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            return _source ??= new CancellationTokenSource();
        }
    }

    private static void CancelAndDispose(CancellationTokenSource? source)
    {
        if (source is null)
            return;

        try { source.Cancel(); }
        catch (ObjectDisposedException) { }

        source.Dispose();
    }
}
