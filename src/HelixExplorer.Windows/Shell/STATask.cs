namespace HelixExplorer.Windows.Shell;

/// <summary>
/// Runs work on a dedicated Single-Threaded Apartment (STA) thread. This is required for reliable
/// COM interop with apartment-threaded shell objects such as <see cref="IShellFolder"/>.
/// </summary>
internal static class STATask
{
    public static Task Run(Action action, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource();

        if (cancellationToken.IsCancellationRequested)
        {
            tcs.SetCanceled(cancellationToken);
            return tcs.Task;
        }

        var thread = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                action();
                tcs.TrySetResult();
            }
            catch (OperationCanceledException)
            {
                tcs.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Helix STA"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    public static Task<T> Run<T>(Func<T> func, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<T>();

        if (cancellationToken.IsCancellationRequested)
        {
            tcs.SetCanceled(cancellationToken);
            return tcs.Task;
        }

        var thread = new Thread(() =>
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
        })
        {
            IsBackground = true,
            Name = "Helix STA"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
