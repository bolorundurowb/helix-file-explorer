using System.Runtime.InteropServices;
using HelixExplorer.Core.FileSystem;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static Vanara.PInvoke.Kernel32;
using static Vanara.PInvoke.Shell32;
using static Vanara.PInvoke.User32;

namespace HelixExplorer.Windows.Shell;

/// <summary>
/// Listens for <c>SHCNE_ASSOCCHANGED</c> on a message-only window. The shell posts that
/// notification to a window that pumps messages, so the pump lives on its own thread.
/// </summary>
public sealed class WinFileAssociationWatcher(ILogger<WinFileAssociationWatcher> logger) : IFileAssociationWatcher
{
    private readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(200);
    private readonly object _gate = new();
    private Thread? _thread;
    private NativeWindow? _window;
    private PIDL? _desktopPidl;
    private uint _registration;
    private uint _messageId;
    private CancellationTokenSource? _debounceCts;
    private int _disposed;

    public event EventHandler? AssociationsChanged;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        lock (_gate)
        {
            if (_thread is not null)
                return;

            var ready = new ManualResetEventSlim(false);
            var thread = new Thread(() => Pump(ready))
            {
                IsBackground = true,
                Name = "Helix shell associations"
            };
            thread.SetApartmentState(ApartmentState.STA);
            _thread = thread;
            thread.Start();
            ready.Wait(TimeSpan.FromSeconds(5));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Thread? thread;
        lock (_gate)
        {
            try { _debounceCts?.Cancel(); } catch (ObjectDisposedException) { }
            _debounceCts?.Dispose();
            _debounceCts = null;
            thread = _thread;

            if (_window is not null && !_window.Hwnd.IsNull)
                PostMessage(_window.Hwnd, (uint)WindowMessage.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        thread?.Join(TimeSpan.FromSeconds(2));
    }

    private void Pump(ManualResetEventSlim ready)
    {
        try
        {
            _messageId = RegisterWindowMessage("HelixExplorer.FileAssociations");
            if (_messageId == 0)
                _messageId = (uint)WindowMessage.WM_USER + 0x100;

            _window = new NativeWindow(WndProc);
            if (!TryRegister(_window.Hwnd))
                logger.LogWarning("Failed to register for shell association changes");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to start shell association watcher");
            Deregister();
            _window?.Dispose();
            _window = null;
            return;
        }
        finally
        {
            ready.Set();
        }

        if (_window is null)
            return;

        try
        {
            while (GetMessage(out var msg, HWND.NULL, 0, 0) > 0)
            {
                TranslateMessage(in msg);
                DispatchMessage(in msg);
            }
        }
        finally
        {
            Deregister();
            _window.Dispose();
            _window = null;
        }
    }

    private bool TryRegister(HWND hwnd)
    {
        _desktopPidl = KNOWNFOLDERID.FOLDERID_Desktop.PIDL();
        SHChangeNotifyEntry[] entries = [new(_desktopPidl, false)];
        _registration = SHChangeNotifyRegister(
            hwnd,
            SHCNRF.SHCNRF_ShellLevel | SHCNRF.SHCNRF_NewDelivery,
            SHCNE.SHCNE_ASSOCCHANGED,
            _messageId,
            entries);
        return _registration != 0;
    }

    private void Deregister()
    {
        if (_registration != 0)
        {
            SHChangeNotifyDeregister(_registration);
            _registration = 0;
        }

        _desktopPidl?.Dispose();
        _desktopPidl = null;
    }

    private IntPtr WndProc(HWND hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == (uint)WindowMessage.WM_CLOSE)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        if (msg == _messageId)
        {
            // NewDelivery hands the shell a shared block. Unlocking it is required even when
            // the event carries no pidl, which association changes do not.
            // This window is registered only for association changes, so every delivery is one.
            var hLock = SHChangeNotification_Lock(wParam, (uint)lParam.ToInt32(), out _, out _);
            try
            {
                ScheduleNotify();
            }
            finally
            {
                if (hLock != default)
                    SHChangeNotification_Unlock(hLock);
            }

            return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void ScheduleNotify()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        CancellationTokenSource cts;
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            try { _debounceCts?.Cancel(); } catch (ObjectDisposedException) { }
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            cts = _debounceCts;
        }

        _ = DebounceAsync(cts);
    }

    private async Task DebounceAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(_debounce, cts.Token).ConfigureAwait(false);
            if (!cts.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
                AssociationsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class NativeWindow : IDisposable
    {
        private readonly WindowProc _wndProc;
        private readonly string _className;
        private readonly HINSTANCE _hInstance;
        private bool _disposed;

        public NativeWindow(WindowProc handler)
        {
            _wndProc = handler;
            _className = "HelixAssocWatcher_" + Guid.NewGuid().ToString("N");
            _hInstance = GetModuleHandle();

            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = _wndProc,
                hInstance = _hInstance,
                lpszClassName = _className
            };

            var atom = RegisterClassEx(wc);
            if (atom.IsInvalid)
                throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");

            Hwnd = CreateWindowEx(
                0,
                _className,
                string.Empty,
                0,
                0, 0, 0, 0,
                HWND.HWND_MESSAGE,
                HMENU.NULL,
                _hInstance,
                IntPtr.Zero);

            if (Hwnd.IsNull)
                throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
        }

        public HWND Hwnd { get; }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (!Hwnd.IsNull)
                DestroyWindow(Hwnd);

            UnregisterClass(_className, _hInstance);
        }
    }
}
