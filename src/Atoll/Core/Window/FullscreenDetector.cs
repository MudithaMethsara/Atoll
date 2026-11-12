using Atoll.Services;

namespace Atoll.Core.Window;

public sealed class FullscreenDetector : IDisposable
{
    private readonly Win32Interop.WinEventDelegate _winEventCallback;
    private IntPtr _foregroundHook;
    private IntPtr _locationHook;
    private IntPtr _ownHwnd;
    private bool _isFullscreen;
    private bool _disposed;

    public bool IsFullscreen => _isFullscreen;
    public event EventHandler<bool>? FullscreenChanged;

    public FullscreenDetector()
    {
        _winEventCallback = OnWinEvent;
    }

    public void Start(IntPtr ownHwnd)
    {
        _ownHwnd = ownHwnd;
        if (_foregroundHook == IntPtr.Zero)
        {
            _foregroundHook = Win32Interop.SetWinEventHook(
                Win32Interop.EVENT_SYSTEM_FOREGROUND,
                Win32Interop.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _winEventCallback,
                0,
                0,
                Win32Interop.WINEVENT_OUTOFCONTEXT | Win32Interop.WINEVENT_SKIPOWNPROCESS);
        }

        if (_locationHook == IntPtr.Zero)
        {
            _locationHook = Win32Interop.SetWinEventHook(
                Win32Interop.EVENT_OBJECT_LOCATIONCHANGE,
                Win32Interop.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero,
                _winEventCallback,
                0,
                0,
                Win32Interop.WINEVENT_OUTOFCONTEXT | Win32Interop.WINEVENT_SKIPOWNPROCESS);
        }

        Evaluate();
    }

    public void Evaluate()
    {
        if (_disposed) return;

        bool nextFullscreen = false;
        try
        {
            IntPtr fg = Win32Interop.GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != _ownHwnd)
            {
                if (Win32Interop.SHQueryUserNotificationState(out var state) == 0)
                {
                    nextFullscreen = state is
                        Win32Interop.QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY or
                        Win32Interop.QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN or
                        Win32Interop.QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE;
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Debug("Fullscreen", $"SHQueryUserNotificationState check failed: {ex.Message}");
        }

        if (nextFullscreen != _isFullscreen)
        {
            _isFullscreen = nextFullscreen;
            FullscreenChanged?.Invoke(this, _isFullscreen);
        }
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (_disposed) return;
        if (eventType == Win32Interop.EVENT_OBJECT_LOCATIONCHANGE)
        {
            if (idObject != 0 || hwnd != Win32Interop.GetForegroundWindow()) return;
        }
        Evaluate();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_foregroundHook != IntPtr.Zero)
        {
            Win32Interop.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
        if (_locationHook != IntPtr.Zero)
        {
            Win32Interop.UnhookWinEvent(_locationHook);
            _locationHook = IntPtr.Zero;
        }
    }
}

