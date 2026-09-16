using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Core.Tests;

public sealed class UiThreadDispatcherTests
{
    /// <summary>
    /// Synchronous stand-in proving <see cref="IUiThreadDispatcher"/> is substitutable in unit tests
    /// without a real UI thread or Avalonia dispatcher.
    /// </summary>
    private sealed class SynchronousUiThreadDispatcher : IUiThreadDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    [Fact]
    public async Task SynchronousDispatcher_RunsWorkInline()
    {
        var dispatcher = new SynchronousUiThreadDispatcher();
        var order = new List<string>();

        dispatcher.Post(() => order.Add("post"));
        await dispatcher.InvokeAsync(() => order.Add("invoke"));

        order[0].Must().Be("post");
        order[1].Must().Be("invoke");
    }

    [Fact]
    public async Task SynchronousDispatcher_GenericInvokeReturnsValue()
    {
        var dispatcher = new SynchronousUiThreadDispatcher();

        (await dispatcher.InvokeAsync(() => 42)).Must().Be(42);
    }
}
