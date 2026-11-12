using System.Windows;
using System.Windows.Interop;
using Atoll.Services;
using Microsoft.Win32;

namespace Atoll.Core.Window;

public sealed class IslandWindowController : IDisposable
{
    private readonly System.Windows.Window _window;
    private readonly DisplayMonitorService _monitorService;
    private readonly Func<Point, bool> _hitTestDip;
    private HwndSource? _hwndSource;
    private bool _disposed;

    public IntPtr Handle { get; private set; }
    public MonitorDescriptor CurrentMonitor { get; private set; }

    public event EventHandler? DisplayOrDpiChanged;
    public event EventHandler? PowerBroadcastReceived;
    public event EventHandler? DeviceChanged;
    public event EventHandler<int>? HotkeyPressed;
    public event EventHandler<int>? TrayMessageReceived;

    public IslandWindowController(
        System.Windows.Window window,
        DisplayMonitorService monitorService,
        Func<Point, bool> hitTestDip)
    {
        _window = window;
        _monitorService = monitorService;
        _hitTestDip = hitTestDip;
        CurrentMonitor = _monitorService.ResolveTargetMonitor(null);
    }

    public void Attach()
    {
        Handle = new WindowInteropHelper(_window).EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(Handle);
        _hwndSource?.AddHook(WndProc);

        ApplyExtendedStyles();
        SystemEvents.DisplaySettingsChanged += OnSystemDisplaySettingsChanged;
    }

    public void PositionOnMonitor(string? monitorDeviceName, double logicalWidth, double logicalHeight, double horizontalOffset)
    {
        if (Handle == IntPtr.Zero) return;

        CurrentMonitor = _monitorService.ResolveTargetMonitor(monitorDeviceName);
        var phys = _monitorService.ComputeOverlayPhysicalRect(CurrentMonitor, logicalWidth, logicalHeight, horizontalOffset);

        Win32Interop.SetWindowPos(
            Handle,
            Win32Interop.HWND_TOPMOST,
            (int)phys.Left,
            (int)phys.Top,
            (int)phys.Width,
            (int)phys.Height,
            Win32Interop.SWP_NOACTIVATE);
    }

    public void EnsureTopmost()
    {
        if (Handle == IntPtr.Zero) return;
        Win32Interop.SetWindowPos(
            Handle,
            Win32Interop.HWND_TOPMOST,
            0, 0, 0, 0,
            Win32Interop.SWP_NOMOVE | Win32Interop.SWP_NOSIZE | Win32Interop.SWP_NOACTIVATE);
    }

    private void ApplyExtendedStyles()
    {
        try
        {
            long exStyle = Win32Interop.GetWindowLongPtr(Handle, Win32Interop.GWL_EXSTYLE).ToInt64();
            exStyle |= Win32Interop.WS_EX_TOOLWINDOW | Win32Interop.WS_EX_NOACTIVATE | Win32Interop.WS_EX_TOPMOST;
            Win32Interop.SetWindowLongPtr(Handle, Win32Interop.GWL_EXSTYLE, new IntPtr(exStyle));
            Win32Interop.SetWindowPos(
                Handle,
                Win32Interop.HWND_TOPMOST,
                0, 0, 0, 0,
                Win32Interop.SWP_NOMOVE | Win32Interop.SWP_NOSIZE | Win32Interop.SWP_NOACTIVATE | Win32Interop.SWP_FRAMECHANGED);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Warn("WindowController", "Failed to apply extended window styles.", ex);
        }
    }

    private void OnSystemDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _window.Dispatcher.BeginInvoke(() => DisplayOrDpiChanged?.Invoke(this, EventArgs.Empty));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Win32Interop.WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(Win32Interop.MA_NOACTIVATE);

            case Win32Interop.WM_NCHITTEST:
            {
                int screenX = (short)(lParam.ToInt64() & 0xFFFF);
                int screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                var clientDip = _window.PointFromScreen(new Point(screenX, screenY));
                if (!_hitTestDip(clientDip))
                {
                    handled = true;
                    return new IntPtr(Win32Interop.HTTRANSPARENT);
                }
                handled = true;
                return new IntPtr(Win32Interop.HTCLIENT);
            }

            case Win32Interop.WM_DPICHANGED:
            case Win32Interop.WM_DISPLAYCHANGE:
                DisplayOrDpiChanged?.Invoke(this, EventArgs.Empty);
                break;

            case Win32Interop.WM_POWERBROADCAST:
                PowerBroadcastReceived?.Invoke(this, EventArgs.Empty);
                break;

            case Win32Interop.WM_DEVICECHANGE:
                int evt = wParam.ToInt32();
                if (evt is Win32Interop.DBT_DEVICEARRIVAL or Win32Interop.DBT_DEVICEREMOVECOMPLETE or Win32Interop.DBT_DEVNODES_CHANGED)
                {
                    DeviceChanged?.Invoke(this, EventArgs.Empty);
                }
                break;

            case Win32Interop.WM_HOTKEY:
                HotkeyPressed?.Invoke(this, wParam.ToInt32());
                handled = true;
                break;

            case Win32Interop.WM_TRAYICON:
                TrayMessageReceived?.Invoke(this, lParam.ToInt32());
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnSystemDisplaySettingsChanged;
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;
    }
}

