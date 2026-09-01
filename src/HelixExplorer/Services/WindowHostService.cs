using HelixExplorer.ViewModels;
using HelixExplorer.Views;
using Microsoft.Extensions.DependencyInjection;

namespace HelixExplorer.Services;

public interface IWindowHostService
{
    Task<MainWindow> OpenWindowAsync(string? initialPath = null, bool restoreSession = false);

    int OpenWindowCount { get; }
}

public sealed class WindowHostService(IServiceScopeFactory scopeFactory) : IWindowHostService
{
    private readonly object _gate = new();
    private readonly List<WindowScope> _scopes = new();

    public int OpenWindowCount
    {
        get
        {
            lock (_gate)
                return _scopes.Count;
        }
    }

    /// <summary>
    /// Disposes any window scopes that did not yet run <see cref="OnWindowClosed"/>.
    /// Call before disposing the root provider so scoped Flush/SaveSession still see live services.
    /// </summary>
    public void Shutdown()
    {
        WindowScope[] remaining;
        lock (_gate)
        {
            remaining = [.. _scopes];
            _scopes.Clear();
        }

        if (remaining.Length == 0)
            return;

        try
        {
            // A window still tracked here never ran its Closed handler (cancelled close, abrupt OS
            // shutdown), so nothing has persisted its session yet. Save from the most recently
            // opened survivor, mirroring the last-window-wins policy in OnWindowClosed, while its
            // scope is still alive. try/finally so a failing save cannot leak the scopes.
            Array.FindLast(remaining, entry => !entry.IsDisposed)?.SaveSession();
        }
        finally
        {
            foreach (var entry in remaining)
            {
                if (entry.TryClaimDisposal())
                    entry.Scope.Dispose();
            }
        }
    }

    public async Task<MainWindow> OpenWindowAsync(string? initialPath = null, bool restoreSession = false)
    {
        var scope = scopeFactory.CreateScope();
        var window = scope.ServiceProvider.GetRequiredService<MainWindow>();
        var vm = scope.ServiceProvider.GetRequiredService<MainWindowViewModel>();
        scope.ServiceProvider.GetRequiredService<IWindowOwnerContext>().SetOwner(window);
        window.DataContext = vm;

        vm.InitializeWindow(restoreSession, initialPath);

        // Capture the instance (and its SaveSession); never re-resolve MainWindowViewModel on close.
        var capturedVm = vm;
        var tracked = new WindowScope(scope, capturedVm.SaveSession);
        window.Closed += (_, _) => CloseTracked(tracked, capturedVm.SaveSession);

        lock (_gate)
            _scopes.Add(tracked);

        window.Show();
        await Task.CompletedTask.ConfigureAwait(true);
        return window;
    }

    /// <summary>Exposed for tests so this path can run without opening an Avalonia window.</summary>
    internal void OnWindowClosed(IServiceScope scope, Action saveSession)
    {
        WindowScope? entry;
        lock (_gate)
            entry = _scopes.Find(tracked => ReferenceEquals(tracked.Scope, scope));

        // No longer tracked means Shutdown() already saved and disposed this scope.
        if (entry is null)
            return;

        CloseTracked(entry, saveSession);
    }

    private void CloseTracked(WindowScope entry, Action saveSession)
    {
        bool isLastWindow;
        lock (_gate)
        {
            _scopes.Remove(entry);
            isLastWindow = _scopes.Count == 0;
        }

        // Shutdown() already saved and disposed this scope if the window closed after quit began.
        if (!entry.TryClaimDisposal())
            return;

        try
        {
            if (isLastWindow)
                saveSession();
        }
        finally
        {
            entry.Scope.Dispose();
        }
    }

    internal void TrackScopeForTests(IServiceScope scope, Action? saveSession = null)
    {
        lock (_gate)
            _scopes.Add(new WindowScope(scope, saveSession ?? (static () => { })));
    }

    /// <summary>
    /// Pairs a window scope with the session-save callback captured when the window opened, so
    /// shutdown can still persist a window that never raised <c>Closed</c>. The disposal claim keeps
    /// a late <c>Closed</c> and <see cref="Shutdown"/> from disposing the same scope twice.
    /// </summary>
    private sealed class WindowScope(IServiceScope scope, Action saveSession)
    {
        private int _disposed;

        public IServiceScope Scope { get; } = scope;

        public Action SaveSession { get; } = saveSession;

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public bool TryClaimDisposal() => Interlocked.Exchange(ref _disposed, 1) == 0;
    }
}
