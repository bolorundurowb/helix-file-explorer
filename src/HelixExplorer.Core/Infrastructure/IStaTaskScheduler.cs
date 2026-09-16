namespace HelixExplorer.Core.Infrastructure;

/// <summary>
/// Abstraction over the single STA worker thread used for apartment-threaded COM interop (shell
/// objects). Substituted with an inline/synchronous scheduler in unit tests.
/// </summary>
public interface IStaTaskScheduler
{
    Task Run(Action action, CancellationToken cancellationToken = default);

    Task<T> Run<T>(Func<T> func, CancellationToken cancellationToken = default);
}
