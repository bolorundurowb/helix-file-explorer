using System.Runtime.InteropServices;
using HelixExplorer.Core.FileSystem;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Windows.FileSystem;

/// <summary>
/// Listens for <c>WM_DEVICECHANGE</c> volume arrival/removal on a message-only HWND.
/// </summary>
public sealed class WinVolumeChangeWatcher(ILogger<WinVolumeChangeWatcher> logger) : IVolumeChangeWatcher
{
    private const int WmDeviceChange = 0x0219;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceRemoveComplete = 0x8004;
    private const int DbtDevTypVolume = 0x00000002;
    private static readonly IntPtr HwndMessage = new(-3);

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

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmDeviceChange)
        {
            var eventType = wParam.ToInt32();
            if (eventType is DbtDeviceArrival or DbtDeviceRemoveComplete && IsVolumeEvent(lParam))
                ScheduleNotify();
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static bool IsVolumeEvent(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero)
            return true;

        try
        {
            var hdr = Marshal.PtrToStructure<DevBroadcastHdr>(lParam);
            return hdr.DeviceType == DbtDevTypVolume || hdr.DeviceType == 0;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct DevBroadcastHdr
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
    }

    private sealed class NativeWindow : IDisposable
    {
        private readonly WndProcDelegate _wndProc;
        private readonly IntPtr _hwnd;
        private readonly string _className;
        private bool _disposed;

        public NativeWindow(WndProcDelegate handler)
        {
            _wndProc = handler;
            _className = "HelixVolumeWatcher_" + Guid.NewGuid().ToString("N");

            var wc = new WndClassEx
            {
                Size = (uint)Marshal.SizeOf<WndClassEx>(),
                LpfnWndProc = _wndProc,
                HInstance = GetModuleHandle(null),
                LpszClassName = _className
            };

            var atom = RegisterClassEx(ref wc);
            if (atom == 0)
                throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");

            _hwnd = CreateWindowEx(
                0,
                _className,
                string.Empty,
                0,
                0, 0, 0, 0,
                HwndMessage,
                IntPtr.Zero,
                wc.HInstance,
                IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
                throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_hwnd != IntPtr.Zero)
                DestroyWindow(_hwnd);

            UnregisterClass(_className, GetModuleHandle(null));
        }
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public WndProcDelegate LpfnWndProc;
        public int CbClsExtra;
        public int CbWndExtra;
        public IntPtr HInstance;
        public IntPtr HIcon;
        public IntPtr HCursor;
        public IntPtr HbrBackground;
        public string? LpszMenuName;
        public string LpszClassName;
        public IntPtr HIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
