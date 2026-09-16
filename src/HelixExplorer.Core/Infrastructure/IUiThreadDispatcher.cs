namespace HelixExplorer.Core.Infrastructure;

/// <summary>
/// Abstraction over the UI thread dispatcher so ViewModels and services can marshal work onto the UI
/// thread without referencing Avalonia, and so tests can substitute a synchronous scheduler.
/// </summary>
public interface IUiThreadDispatcher
{
    bool CheckAccess();

    void Post(Action action);

    Task InvokeAsync(Action action);

    Task<T> InvokeAsync<T>(Func<T> action);
}
