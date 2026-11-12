using System.Management;
using Atoll.Core.Window;

namespace Atoll.Services;

public sealed class BrightnessService : IDisposable
{
    private readonly object _sync = new();
    private ManagementEventWatcher? _watcher;
    private volatile bool _disposed;

    public int CurrentBrightness { get; private set; } = 100;
    public bool IsSupported { get; private set; }

    public event EventHandler<int>? BrightnessChanged;

    public BrightnessService()
    {
        // Probe WMI / DDC-CI off the UI thread so startup stays < 50ms
        _ = Task.Run(() =>
        {
            if (_disposed) return;
            bool supported = TryReadInternalBrightness(out int level) || TryReadDdcBrightness(out level);
            lock (_sync)
            {
                if (_disposed) return;
                IsSupported = supported;
                if (supported)
                {
                    CurrentBrightness = level;
                    StartWmiWatcherLocked();
                }
            }
        });
    }

    public void SetBrightness(int percent)
    {
        int clamped = Math.Clamp(percent, 0, 100);
        if (TrySetInternalBrightness(clamped) || TrySetDdcBrightness(clamped))
        {
            CurrentBrightness = clamped;
            BrightnessChanged?.Invoke(this, CurrentBrightness);
        }
    }

    public void AdjustBrightness(int delta)
    {
        SetBrightness(CurrentBrightness + delta);
    }

    private void StartWmiWatcherLocked()
    {
        if (_disposed || _watcher is not null) return;
        try
        {
            var scope = new ManagementScope(@"\\.\root\wmi");
            var query = new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent");
            _watcher = new ManagementEventWatcher(scope, query);
            _watcher.EventArrived += OnWmiBrightnessEventArrived;
            _watcher.Start();
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Debug("Brightness", $"WMI brightness watcher unavailable (desktop monitor likely): {ex.Message}");
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    private void OnWmiBrightnessEventArrived(object sender, EventArrivedEventArgs args)
    {
        try
        {
            using var evt = args.NewEvent;
            if (evt?["Brightness"] is byte b)
            {
                IsSupported = true;
                CurrentBrightness = b;
                BrightnessChanged?.Invoke(this, b);
            }
        }
        catch
        {
            // Ignore malformed WMI event
        }
    }

    private static bool TryReadInternalBrightness(out int level)
    {
        level = 100;
        try
        {
            using var searcher = new ManagementObjectSearcher(@"\\.\root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    if (obj["CurrentBrightness"] is byte b)
                    {
                        level = b;
                        return true;
                    }
                }
            }
        }
        catch
        {
            // No WMI internal backlight
        }
        return false;
    }

    private static bool TrySetInternalBrightness(int level)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"\\.\root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    obj.InvokeMethod("WmiSetBrightness", new object[] { 1u, (byte)level });
                    return true;
                }
            }
        }
        catch
        {
            // Fallback to DDC/CI
        }
        return false;
    }

    private static bool TryReadDdcBrightness(out int level)
    {
        level = 100;
        try
        {
            IntPtr hMon = Win32Interop.MonitorFromPoint(new Win32Interop.POINT { X = 0, Y = 0 }, Win32Interop.MONITOR_DEFAULTTONEAREST);
            if (!Win32Interop.GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out uint count) || count == 0) return false;

            var monitors = new Win32Interop.PHYSICAL_MONITOR[count];
            if (!Win32Interop.GetPhysicalMonitorsFromHMONITOR(hMon, count, monitors)) return false;

            try
            {
                if (Win32Interop.GetMonitorBrightness(monitors[0].hPhysicalMonitor, out uint min, out uint cur, out uint max) && max > min)
                {
                    level = (int)Math.Round((cur - min) * 100.0 / (max - min));
                    return true;
                }
            }
            finally
            {
                Win32Interop.DestroyPhysicalMonitors(count, monitors);
            }
        }
        catch
        {
            // DDC/CI unsupported
        }
        return false;
    }

    private static bool TrySetDdcBrightness(int level)
    {
        try
        {
            IntPtr hMon = Win32Interop.MonitorFromPoint(new Win32Interop.POINT { X = 0, Y = 0 }, Win32Interop.MONITOR_DEFAULTTONEAREST);
            if (!Win32Interop.GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out uint count) || count == 0) return false;

            var monitors = new Win32Interop.PHYSICAL_MONITOR[count];
            if (!Win32Interop.GetPhysicalMonitorsFromHMONITOR(hMon, count, monitors)) return false;

            try
            {
                if (Win32Interop.GetMonitorBrightness(monitors[0].hPhysicalMonitor, out uint min, out _, out uint max) && max > min)
                {
                    uint target = min + (uint)Math.Round((max - min) * (level / 100.0));
                    return Win32Interop.SetMonitorBrightness(monitors[0].hPhysicalMonitor, target);
                }
            }
            finally
            {
                Win32Interop.DestroyPhysicalMonitors(count, monitors);
            }
        }
        catch
        {
            // Ignore
        }
        return false;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_watcher is not null)
                {
                    _watcher.EventArrived -= OnWmiBrightnessEventArrived;
                    _watcher.Stop();
                    _watcher.Dispose();
                }
            }
            catch
            {
                // Ignore
            }
            _watcher = null;
        }
    }
}

