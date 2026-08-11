using System.Runtime.InteropServices;
using HelixExplorer.Core.FileSystem;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static Vanara.PInvoke.Kernel32;
using static Vanara.PInvoke.User32;

namespace HelixExplorer.Windows.FileSystem;

/// <summary>
/// Listens for <c>WM_DEVICECHANGE</c> volume arrival/removal on a message-only HWND.
/// </summary>
public sealed class WinVolumeChangeWatcher(ILogger<WinVolumeChangeWatcher> logger) : IVolumeChangeWatcher
{
    private readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(400);
    private readonly object _gate = new();
    private NativeWindow? _window;
    private CancellationTokenSource? _debounceCts;
    private bool _disposed;

    public event EventHandler? VolumesChanged;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (_window is not null)
                return;

            try
            {
                _window = new NativeWindow(WndProc);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to start volume change watcher");
            }
        }
    }

    private IntPtr WndProc(HWND hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == (uint)WindowMessage.WM_DEVICECHANGE)
        {
            var eventType = (DeviceBroadcastEvent)wParam.ToInt32();
            if (eventType is DeviceBroadcastEvent.DBT_DEVICEARRIVAL or DeviceBroadcastEvent.DBT_DEVICEREMOVECOMPLETE
                && IsVolumeEvent(lParam))
            {
                ScheduleNotify();
            }
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static bool IsVolumeEvent(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero)
            return true;

        try
        {
            var hdr = Marshal.PtrToStructure<DEV_BROADCAST_HDR>(lParam);
            return hdr.dbch_devicetype is DBT_DEVTYPE.DBT_DEVTYP_VOLUME or 0;
        }
        catch
        {
            return true;
        }
    }

    private void ScheduleNotify()
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
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
            if (!cts.IsCancellationRequested && !_disposed)
                VolumesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        lock (_gate)
        {
            try { _debounceCts?.Cancel(); } catch (ObjectDisposedException) { }
            _debounceCts?.Dispose();
            _debounceCts = null;
            _window?.Dispose();
            _window = null;
        }
    }

    private sealed class NativeWindow : IDisposable
    {
        private readonly WindowProc _wndProc;
        private readonly HWND _hwnd;
        private readonly string _className;
        private readonly HINSTANCE _hInstance;
        private bool _disposed;

        public NativeWindow(WindowProc handler)
        {
            _wndProc = handler;
            _className = "HelixVolumeWatcher_" + Guid.NewGuid().ToString("N");
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

            _hwnd = CreateWindowEx(
                0,
                _className,
                string.Empty,
                0,
                0, 0, 0, 0,
                HWND.HWND_MESSAGE,
                HMENU.NULL,
                _hInstance,
                IntPtr.Zero);

            if (_hwnd.IsNull)
                throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (!_hwnd.IsNull)
                DestroyWindow(_hwnd);

            UnregisterClass(_className, _hInstance);
        }
    }
}
