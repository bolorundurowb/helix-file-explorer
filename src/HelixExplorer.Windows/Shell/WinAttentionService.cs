using System.Runtime.InteropServices;
using HelixExplorer.Core.Infrastructure;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static Vanara.PInvoke.Shell32;
using static Vanara.PInvoke.User32;
using Timer = System.Threading.Timer;

namespace HelixExplorer.Windows.Shell;

/// <summary>
/// Taskbar progress (<c>ITaskbarList3</c>), taskbar flash, and a notification-area balloon for one window.
/// </summary>
/// <remarks>
/// <c>ITaskbarList3</c> is apartment-threaded, so progress calls must come from the UI thread that
/// created it; the status centre raises them there. The balloon is used instead of a WinRT toast
/// because a toast needs a registered AppUserModelID and shortcut, which a portable run does not have;
/// Windows 10 and later render the balloon as a toast anyway.
/// </remarks>
public sealed class WinAttentionService(IUiHost uiHost, ILogger<WinAttentionService> logger)
    : IAttentionService, IDisposable
{
    private const uint NotifyIconId = 1;
    private const ulong ProgressTotal = 100;

    // Long enough for the toast to be read; the icon is removed afterwards so the tray does not keep
    // a stray Helix entry after every operation.
    private static readonly TimeSpan NotifyIconLifetime = TimeSpan.FromSeconds(10);

    private readonly object _notifyGate = new();
    private ITaskbarList3? _taskbar;
    private bool _taskbarUnavailable;
    private TBPFLAG _state = TBPFLAG.TBPF_NOPROGRESS;
    private int _percent = -1;
    private HWND _notifyIconOwner;
    private Timer? _notifyIconTimer;
    private bool _disposed;

    public void ShowProgress(double? fraction)
    {
        if (fraction is not { } value)
        {
            SetState(TBPFLAG.TBPF_INDETERMINATE);
            return;
        }

        // Report fires per item; the taskbar cannot show finer than a percent, so skip the COM call
        // for updates that would not change the bar.
        var percent = (int)(Math.Clamp(value, 0, 1) * ProgressTotal);
        if (_state == TBPFLAG.TBPF_NORMAL && percent == _percent)
            return;

        if (!TryGetTaskbar(out var taskbar, out var hwnd))
            return;

        try
        {
            taskbar.SetProgressValue(hwnd, (ulong)percent, ProgressTotal);
            _state = TBPFLAG.TBPF_NORMAL;
            _percent = percent;
        }
        catch (COMException ex)
        {
            logger.LogDebug(ex, "SetProgressValue failed");
        }
    }

    public void ShowError()
    {
        if (!TryGetTaskbar(out var taskbar, out var hwnd))
            return;

        try
        {
            // An indeterminate or barely started operation has no visible bar, so fill it; otherwise
            // the error state would be invisible.
            taskbar.SetProgressState(hwnd, TBPFLAG.TBPF_ERROR);
            taskbar.SetProgressValue(hwnd, ProgressTotal, ProgressTotal);
            _state = TBPFLAG.TBPF_ERROR;
            _percent = -1;
        }
        catch (COMException ex)
        {
            logger.LogDebug(ex, "SetProgressState(error) failed");
        }
    }

    public void ClearProgress() => SetState(TBPFLAG.TBPF_NOPROGRESS);

    public void NotifyIfInactive(string title, string message, bool isError)
    {
        var hwnd = GetWindow();
        if (hwnd.IsNull || IsForeground(hwnd))
            return;

        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hwnd = hwnd,
            uID = NotifyIconId,
            uFlags = NIF.NIF_TIP | NIF.NIF_INFO,
            szTip = title,
            szInfoTitle = title,
            szInfo = message,
            dwInfoFlags = (isError ? NIIF.NIIF_ERROR : NIIF.NIIF_INFO) | NIIF.NIIF_RESPECT_QUIET_TIME
        };

        var icon = GetWindowIcon(hwnd);
        if (icon != IntPtr.Zero)
        {
            data.hIcon = icon;
            data.uFlags |= NIF.NIF_ICON;
        }

        lock (_notifyGate)
        {
            if (_disposed)
                return;

            try
            {
                var shown = !_notifyIconOwner.IsNull
                    ? Shell_NotifyIcon(NIM.NIM_MODIFY, in data)
                    : Shell_NotifyIcon(NIM.NIM_ADD, in data);
                if (!shown)
                    return;

                _notifyIconOwner = hwnd;
                _notifyIconTimer ??= new Timer(_ => RemoveNotifyIcon());
                _notifyIconTimer.Change(NotifyIconLifetime, Timeout.InfiniteTimeSpan);
            }
            catch (Exception ex) when (ex is COMException or EntryPointNotFoundException)
            {
                logger.LogDebug(ex, "Shell_NotifyIcon failed");
            }
        }
    }

    public void FlashIfInactive()
    {
        var hwnd = GetWindow();
        if (hwnd.IsNull || IsForeground(hwnd))
            return;

        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = hwnd,
            dwFlags = FLASHW.FLASHW_TRAY | FLASHW.FLASHW_TIMERNOFG,
            uCount = 0,
            dwTimeout = 0
        };
        FlashWindowEx(in info);
    }

    public void Dispose()
    {
        lock (_notifyGate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        RemoveNotifyIcon();
        _notifyIconTimer?.Dispose();

        if (_taskbar is not null)
        {
            Marshal.FinalReleaseComObject(_taskbar);
            _taskbar = null;
        }
    }

    private void SetState(TBPFLAG state)
    {
        if (_state == state)
            return;

        if (!TryGetTaskbar(out var taskbar, out var hwnd))
            return;

        try
        {
            taskbar.SetProgressState(hwnd, state);
            _state = state;
            _percent = -1;
        }
        catch (COMException ex)
        {
            logger.LogDebug(ex, "SetProgressState failed");
        }
    }

    private bool TryGetTaskbar(out ITaskbarList3 taskbar, out HWND hwnd)
    {
        taskbar = null!;
        hwnd = GetWindow();
        if (hwnd.IsNull || _taskbarUnavailable || _disposed)
            return false;

        if (_taskbar is null)
        {
            try
            {
                var created = (ITaskbarList3)new CTaskbarList();
                created.HrInit();
                _taskbar = created;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                // Explorer replaced by a shell without a taskbar; stop retrying for this window.
                _taskbarUnavailable = true;
                logger.LogDebug(ex, "ITaskbarList3 unavailable");
                return false;
            }
        }

        taskbar = _taskbar;
        return true;
    }

    private HWND GetWindow()
    {
        try
        {
            return uiHost.GetMainWindowHandle();
        }
        catch (InvalidOperationException)
        {
            // Asked off the UI thread or after the window closed.
            return HWND.NULL;
        }
    }

    /// <summary>
    /// True when this window or a dialog it owns is in front. The conflict dialog is an owned window,
    /// so comparing the foreground handle directly would treat the user answering it as away.
    /// </summary>
    private static bool IsForeground(HWND hwnd)
    {
        var foreground = GetForegroundWindow();
        if (foreground.IsNull)
            return false;

        return foreground == hwnd || GetAncestor(foreground, GetAncestorFlag.GA_ROOTOWNER) == hwnd;
    }

    private static IntPtr GetWindowIcon(HWND hwnd)
    {
        var icon = SendMessage(hwnd, (uint)WindowMessage.WM_GETICON, (IntPtr)WM_ICON_WPARAM.ICON_SMALL2, IntPtr.Zero);
        if (icon == IntPtr.Zero)
            icon = SendMessage(hwnd, (uint)WindowMessage.WM_GETICON, (IntPtr)WM_ICON_WPARAM.ICON_BIG, IntPtr.Zero);
        return icon;
    }

    private void RemoveNotifyIcon()
    {
        lock (_notifyGate)
        {
            if (_notifyIconOwner.IsNull)
                return;

            var data = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hwnd = _notifyIconOwner,
                uID = NotifyIconId
            };

            try
            {
                Shell_NotifyIcon(NIM.NIM_DELETE, in data);
            }
            catch (Exception ex) when (ex is COMException or EntryPointNotFoundException)
            {
                logger.LogDebug(ex, "Shell_NotifyIcon delete failed");
            }

            _notifyIconOwner = HWND.NULL;
        }
    }
}
