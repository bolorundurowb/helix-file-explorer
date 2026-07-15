namespace HelixExplorer.ViewModels.Pane;

/// <summary>
/// Runs per-item async work (icon/thumbnail loading) with a bounded degree of concurrency and prompt
/// cancellation. Items are dispatched in order; once cancellation is requested no further items are
/// started, so a stale listing cannot keep decoding bitmaps in the background.
/// </summary>
public sealed class BoundedVisualLoader(int maxConcurrency)
{
    private readonly int _maxConcurrency = maxConcurrency < 1 ? 1 : maxConcurrency;

    public int MaxConcurrency => _maxConcurrency;

    public async Task RunAsync<T>(
        IReadOnlyList<T> items,
        Func<T, CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return;

        using var throttle = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
        var running = new List<Task>(Math.Min(items.Count, _maxConcurrency * 2));

        foreach (var item in items)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throttle.Release();
                break;
            }

            running.Add(RunOneAsync(item, work, throttle, cancellationToken));
        }

        try
        {
            await Task.WhenAll(running).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when work observes cancellation; individual failures are the worker's concern.
        }
    }

    private static async Task RunOneAsync<T>(
        T item,
        Func<T, CancellationToken, Task> work,
        SemaphoreSlim throttle,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!cancellationToken.IsCancellationRequested)
                await work(item, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is normal when a newer listing supersedes this one.
        }
        finally
        {
            throttle.Release();
        }
    }
}
