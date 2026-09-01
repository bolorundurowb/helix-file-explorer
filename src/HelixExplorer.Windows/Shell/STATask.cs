using System.Collections.Concurrent;

namespace HelixExplorer.Windows.Shell;

/// <summary>
/// Runs shell COM work on one process-long, message-pumping STA.
/// </summary>
internal static class STATask
{
    private static readonly Lazy<StaWorker> Worker = new(
        static () => new StaWorker(),
        LazyThreadSafetyMode.ExecutionAndPublication);

    static STATask()
    {
        AppDomain.CurrentDomain.ProcessExit += static (_, _) =>
        {
            if (Worker.IsValueCreated)
                Worker.Value.Dispose();
        };
    }

    public static Task Run(Action action, CancellationToken cancellationToken = default)
        => Worker.Value.Enqueue(
            () =>
            {
                action();
                return true;
            },
            cancellationToken);

    public static Task<T> Run<T>(Func<T> func, CancellationToken cancellationToken = default)
        => Worker.Value.Enqueue(func, cancellationToken);

    private sealed class StaWorker : IDisposable
    {
        private readonly BlockingCollection<IWorkItem> _queue = new();
        private readonly Thread _thread;
        private int _disposed;

        public StaWorker()
        {
            _thread = new Thread(RunLoop)
            {
                IsBackground = true,
                Name = "Helix Shell STA"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        public Task<T> Enqueue<T>(Func<T> action, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<T>(cancellationToken);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            var item = new WorkItem<T>(action, cancellationToken);
            try
            {
                _queue.Add(item, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                item.Cancel();
            }
            catch (InvalidOperationException)
            {
                item.DisposeWorker();
            }

            return item.Task;
        }

        private void RunLoop()
        {
            while (!_queue.IsCompleted)
            {
                if (_queue.TryTake(out var item, millisecondsTimeout: 20))
                    item.Execute();

                PumpMessages();
            }

            while (_queue.TryTake(out var remaining))
                remaining.DisposeWorker();
        }

        private static void PumpMessages()
        {
            while (Vanara.PInvoke.User32.PeekMessage(
                       out var message,
                       Vanara.PInvoke.HWND.NULL,
                       0,
                       0,
                       Vanara.PInvoke.User32.PM.PM_REMOVE))
            {
                Vanara.PInvoke.User32.TranslateMessage(message);
                Vanara.PInvoke.User32.DispatchMessage(message);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _queue.CompleteAdding();
            if (Environment.CurrentManagedThreadId != _thread.ManagedThreadId)
                _thread.Join(TimeSpan.FromSeconds(2));
            _queue.Dispose();
        }
    }

    private interface IWorkItem
    {
        void Execute();
        void DisposeWorker();
    }

    private sealed class WorkItem<T> : IWorkItem
    {
        private readonly Func<T> _action;
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<T> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _registration;

        public WorkItem(Func<T> action, CancellationToken cancellationToken)
        {
            _action = action;
            _cancellationToken = cancellationToken;
            _registration = cancellationToken.Register(
                static state => ((WorkItem<T>)state!).Cancel(),
                this);
        }

        public Task<T> Task => _completion.Task;

        public void Execute()
        {
            if (_completion.Task.IsCompleted)
            {
                _registration.Dispose();
                return;
            }

            try
            {
                _cancellationToken.ThrowIfCancellationRequested();
                _completion.TrySetResult(_action());
            }
            catch (OperationCanceledException)
            {
                Cancel();
            }
            catch (Exception ex)
            {
                _completion.TrySetException(ex);
            }
            finally
            {
                _registration.Dispose();
            }
        }

        public void Cancel() => _completion.TrySetCanceled(_cancellationToken);

        public void DisposeWorker()
        {
            _completion.TrySetException(new ObjectDisposedException(nameof(STATask)));
            _registration.Dispose();
        }
    }
}
