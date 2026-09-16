using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Windows.Shell;

/// <summary>
/// Public adapter over the internal STA worker thread so consumers depend on
/// <see cref="IStaTaskScheduler"/> instead of the concrete <see cref="STATask"/>.
/// </summary>
public sealed class StaTaskScheduler : IStaTaskScheduler
{
    public Task Run(Action action, CancellationToken cancellationToken = default)
        => STATask.Run(action, cancellationToken);

    public Task<T> Run<T>(Func<T> func, CancellationToken cancellationToken = default)
        => STATask.Run(func, cancellationToken);
}
