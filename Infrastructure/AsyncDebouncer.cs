namespace HelixExplorer.Infrastructure;

/// <summary>
/// Throttles frequent invocations down to a single trailing action after the
/// configured quiet period. Outstanding callbacks are cancelled atomically via a
/// <see cref="CancellationTokenSource"/>, eliminating racing refresh waves.
/// </summary>
public sealed class AsyncDebouncer : IDisposable
{
    private readonly TimeSpan _delay;
    private CancellationTokenSource? _cts;
    private readonly object _gate = new();
    private bool _disposed;

    public AsyncDebouncer(TimeSpan delay) => _delay = delay;

    /// <summary>Schedules <paramref name="action"/> to run after the quiet period.
    /// Any previously scheduled invocation is cancelled.</summary>
    public void Schedule(Func<CancellationToken, Task> action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = ScheduleCore(action, token);
        }
    }

    private async Task ScheduleCore(Func<CancellationToken, Task> action, CancellationToken token)
    {
        try
        {
            await Task.Delay(_delay, token).ConfigureAwait(false);
            if (!token.IsCancellationRequested)
            {
                await action(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AsyncDebouncer action threw: {ex.Message}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}