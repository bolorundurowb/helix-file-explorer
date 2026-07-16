using HelixExplorer.Core.Infrastructure;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HelixExplorer.Core.Tests;

public sealed class FireAndForgetSafeTests
{
    [Fact]
    public async Task Run_swallows_operation_canceled_exception()
    {
        var tcs = new TaskCompletionSource();
        var logger = new TestLogger(onLog: tcs);

        FireAndForgetSafe.Run(
            () => throw new OperationCanceledException(),
            logger);

        // OperationCanceledException is swallowed, so the logger should never be called.
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.NotEqual(tcs.Task, completed);

        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task Run_logs_unexpected_exception()
    {
        var tcs = new TaskCompletionSource();
        var logger = new TestLogger(onLog: tcs);
        var exception = new InvalidOperationException("boom");

        FireAndForgetSafe.Run(
            () => throw exception,
            logger);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(logger.Errors);
        Assert.Same(exception, logger.Errors[0].Exception);
        Assert.Contains("boom", exception.Message);
    }

    [Fact]
    public async Task Run_task_logs_unexpected_exception()
    {
        var tcs = new TaskCompletionSource();
        var logger = new TestLogger(onLog: tcs);
        var exception = new InvalidOperationException("task boom");

        FireAndForgetSafe.Run(
            Task.Run(() => throw exception),
            logger);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(logger.Errors);
        Assert.Same(exception, logger.Errors[0].Exception);
    }

    private sealed class TestLogger(TaskCompletionSource? onLog = null) : ILogger
    {
        public readonly List<(Exception? Exception, string Message)> Errors = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                Errors.Add((exception, formatter(state, exception)));
                onLog?.TrySetResult();
            }
        }
    }
}
