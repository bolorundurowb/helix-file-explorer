using Avalonia.Threading;
using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Services;

public sealed class AvaloniaUiThreadDispatcher : IUiThreadDispatcher
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public Task InvokeAsync(Action action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();

    public Task<T> InvokeAsync<T>(Func<T> action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();

    public static void ObserveUnhandledExceptions(Action<Exception> report)
        => Dispatcher.UIThread.UnhandledException += (_, e) => report(e.Exception);
}
