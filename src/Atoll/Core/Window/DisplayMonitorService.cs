using System.Runtime.InteropServices;
using System.Windows;

namespace Atoll.Core.Window;

public sealed record MonitorDescriptor(
    IntPtr Handle,
    string DeviceName,
    bool IsPrimary,
    Rect PhysicalBounds,
    Rect PhysicalWorkArea,
    uint DpiX,
    uint DpiY)
{
    public double ScaleX => DpiX / 96.0;
    public double ScaleY => DpiY / 96.0;
    public string DisplayLabel => IsPrimary ? $"{DeviceName} (Primary)" : DeviceName;
}

public sealed class DisplayMonitorService
{
    public IReadOnlyList<MonitorDescriptor> GetMonitors()
    {
        var list = new List<MonitorDescriptor>();
        Win32Interop.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr _, ref Win32Interop.RECT _, IntPtr _) =>
        {
            var info = new Win32Interop.MONITORINFOEX { cbSize = Marshal.SizeOf<Win32Interop.MONITORINFOEX>() };
            if (Win32Interop.GetMonitorInfo(hMon, ref info))
            {
                uint dpiX = 96, dpiY = 96;
                try
                {
                    if (Win32Interop.GetDpiForMonitor(hMon, Win32Interop.MDT_EFFECTIVE_DPI, out uint dx, out uint dy) == 0 && dx > 0)
                    {
                        dpiX = dx;
                        dpiY = dy;
                    }
                }
                catch
                {
                    // Fallback to 96 DPI on pre-8.1/edge cases
                }

                var bounds = new Rect(
                    info.rcMonitor.Left,
                    info.rcMonitor.Top,
                    info.rcMonitor.Right - info.rcMonitor.Left,
                    info.rcMonitor.Bottom - info.rcMonitor.Top);

                var work = new Rect(
                    info.rcWork.Left,
                    info.rcWork.Top,
                    info.rcWork.Right - info.rcWork.Left,
                    info.rcWork.Bottom - info.rcWork.Top);

                bool isPrimary = (info.dwFlags & 1) != 0;
                list.Add(new MonitorDescriptor(hMon, info.szDevice, isPrimary, bounds, work, dpiX, dpiY));
            }
            return true;
        }, IntPtr.Zero);

        if (list.Count == 0)
        {
            list.Add(new MonitorDescriptor(
                IntPtr.Zero,
                "PRIMARY",
                true,
                new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight),
                SystemParameters.WorkArea,
                96,
                96));
        }

        return list;
    }

    public MonitorDescriptor ResolveTargetMonitor(string? preferredDeviceName)
    {
        var monitors = GetMonitors();
        if (!string.IsNullOrWhiteSpace(preferredDeviceName))
        {
            var match = monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, preferredDeviceName, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }

    public Rect ComputeOverlayPhysicalRect(
        MonitorDescriptor monitor,
        double logicalOverlayWidth,
        double logicalOverlayHeight,
        double horizontalOffsetDip)
    {
        double physWidth = Math.Round(logicalOverlayWidth * monitor.ScaleX);
        double physHeight = Math.Round(logicalOverlayHeight * monitor.ScaleY);
        double physOffsetX = Math.Round(horizontalOffsetDip * monitor.ScaleX);

        double left = monitor.PhysicalBounds.Left + (monitor.PhysicalBounds.Width - physWidth) * 0.5 + physOffsetX;
        double top = monitor.PhysicalBounds.Top;

        return new Rect(Math.Round(left), Math.Round(top), physWidth, physHeight);
    }
}

