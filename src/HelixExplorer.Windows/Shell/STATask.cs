using System.Collections.Concurrent;

namespace HelixExplorer.Windows.Shell;

/// <summary>
/// Runs work on a single, long-lived Single-Threaded Apartment (STA) thread. STA is required for
/// reliable COM interop with apartment-threaded shell objects such as <see cref="Vanara.PInvoke.Shell32.IShellFolder"/>.
/// Reusing one worker avoids paying a new-thread creation cost (stack reservation plus startup) on
/// every shell call, which the listing, recycle-bin, and restore paths used to incur.
/// </summary>
internal static class STATask
{
    private static readonly BlockingCollection<Action> Queue = new();
    private static readonly Thread Worker = CreateWorker();

    public static Task Run(Action action, CancellationToken cancellationToken = default)
        => Enqueue(() => { action(); return 0; }, cancellationToken);

    public static Task<T> Run<T>(Func<T> func, CancellationToken cancellationToken = default)
        => Enqueue(func, cancellationToken);

    private static Thread CreateWorker()
    {
        var thread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "Helix STA"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    }

    private static void WorkerLoop()
    {
        foreach (var work in Queue.GetConsumingEnumerable())
            work();
    }

    private static Task<T> Enqueue<T>(Func<T> func, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<T>();

        if (cancellationToken.IsCancellationRequested)
        {
            tcs.SetCanceled(cancellationToken);
            return tcs.Task;
        }

        Queue.Add(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                tcs.TrySetResult(func());
            }
            catch (OperationCanceledException)
            {
                tcs.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }
}
