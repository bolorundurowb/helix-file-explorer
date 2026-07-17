using HelixExplorer.Core.Infrastructure;
using Microsoft.Extensions.Logging;

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

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        ReferenceEquals(completed, tcs.Task).Must().BeFalse();

        logger.Errors.Must().BeEmpty();
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

        logger.Errors.Must().HaveCount(1);
        logger.Errors[0].Exception.Must().Be(exception);
        exception.Message.Must().Contain("boom");
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

        logger.Errors.Must().HaveCount(1);
        logger.Errors[0].Exception.Must().Be(exception);
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
