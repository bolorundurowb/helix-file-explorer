using HelixExplorer.ViewModels.Pane;

namespace HelixExplorer.ViewModels.Tests;

public class BoundedVisualLoaderTests
{
    [Fact]
    public async Task RunAsync_NeverExceedsMaxConcurrency()
    {
        const int max = 4;
        var loader = new BoundedVisualLoader(max);
        var items = Enumerable.Range(0, 100).ToList();

        var current = 0;
        var peak = 0;
        var gate = new object();

        await loader.RunAsync(items, async (_, ct) =>
        {
            lock (gate)
            {
                current++;
                if (current > peak)
                    peak = current;
            }

            await Task.Delay(5, ct);

            lock (gate)
                current--;
        }, CancellationToken.None);

        peak.Must().BeLessThanOrEqualTo(max);
        peak.Must().BeGreaterThan(1);
    }

    [Fact]
    public async Task RunAsync_Cancellation_StopsDispatchingFurtherItems()
    {
        var loader = new BoundedVisualLoader(2);
        var items = Enumerable.Range(0, 1000).ToList();
        using var cts = new CancellationTokenSource();

        var started = 0;
        await loader.RunAsync(items, async (_, ct) =>
        {
            var n = Interlocked.Increment(ref started);
            if (n == 3)
                cts.Cancel();

            await Task.Delay(5, ct);
        }, cts.Token);

        started.Must().BeLessThan(1000);
    }

    [Fact]
    public async Task RunAsync_EmptyList_CompletesImmediately()
    {
        var loader = new BoundedVisualLoader(4);
        var ran = false;
        await loader.RunAsync(Array.Empty<int>(), (_, _) => { ran = true; return Task.CompletedTask; }, CancellationToken.None);
        ran.Must().BeFalse();
    }

    [Fact]
    public void Constructor_ClampsMaxConcurrencyToAtLeastOne()
    {
        new BoundedVisualLoader(0).MaxConcurrency.Must().Be(1);
        new BoundedVisualLoader(-5).MaxConcurrency.Must().Be(1);
        new BoundedVisualLoader(8).MaxConcurrency.Must().Be(8);
    }
}
