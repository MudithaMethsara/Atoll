using System.Windows;
using System.Windows.Media;
using Atoll.Core.Activities;
using Atoll.Core.Window;

namespace Atoll.Modules.Battery;

public sealed class BatteryActivity : IslandActivityBase
{
    private int _percent;
    private bool _isCharging;
    private bool _isLow;

    public override ActivityKind Kind => ActivityKind.Battery;
    public override Size PreferredCompactSize => new(184, 36);
    public override Size PreferredExpandedSize => new(340, 128);
    public override Size PreferredAlertSize => new(300, 74);

    public int Percent
    {
        get => _percent;
        internal set
        {
            if (SetField(ref _percent, value))
            {
                Progress = Math.Clamp(value / 100.0, 0.0, 1.0);
                CompactTrailingText = $"{value}%";
            }
        }
    }

    public bool IsCharging
    {
        get => _isCharging;
        internal set => SetField(ref _isCharging, value);
    }

    public bool IsLow
    {
        get => _isLow;
        internal set => SetField(ref _isLow, value);
    }

    internal BatteryActivity() : base("battery.status", "battery")
    {
        Priority = ActivityPriority.Normal;
    }

    internal void UpdateState(int percent, bool isCharging, bool isLow)
    {
        Percent = percent;
        IsCharging = isCharging;
        IsLow = isLow;

        if (isCharging)
        {
            Title = percent >= 100 ? "Fully Charged" : "Charging";
            Subtitle = $"{percent}% battery";
            Glyph = "\uE83E";
            AccentColor = Color.FromRgb(48, 209, 88);
            Priority = ActivityPriority.Low;
        }
        else if (isLow)
        {
            Title = "Low Battery";
            Subtitle = $"{percent}% remaining";
            Glyph = "\uE850";
            AccentColor = Color.FromRgb(255, 69, 58);
            Priority = ActivityPriority.High;
        }
        else
        {
            Title = "Battery";
            Subtitle = $"{percent}%";
            Glyph = "\uE83F";
            AccentColor = Color.FromRgb(48, 209, 88);
            Priority = ActivityPriority.Low;
        }
    }
}

public sealed class BatteryModule : IIslandModule
{
    private ModuleContext? _ctx;
    private BatteryActivity? _activity;
    private IntPtr _acNotifyHandle;
    private IntPtr _pctNotifyHandle;
    private bool _hasBattery;
    private bool _wasCharging;
    private bool _lowAlertSent;
    private bool _initialized;
    private bool _disposed;

    public string ModuleId => "battery";
    public string DisplayName => "Battery & Charging";
    public bool IsEnabled { get; private set; }
    public bool IsActive => _activity is not null && IsEnabled;

    public Task InitializeAsync(ModuleContext context)
    {
        _ctx = context;
        IsEnabled = context.SettingsService.Current.EnableBattery;
        _ctx.SettingsService.SettingsChanged += OnSettingsChanged;

        RegisterPowerNotifications();
        EvaluateBatteryStatus(suppressInitialAlert: true);
        _initialized = true;
        return Task.CompletedTask;
    }

    public void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled) return;
        IsEnabled = enabled;
        if (!enabled)
        {
            _ctx?.ActivityManager.Remove("battery.status");
            _activity = null;
        }
        else
        {
            EvaluateBatteryStatus(suppressInitialAlert: true);
        }
    }

    public void HandlePowerBroadcast()
    {
        if (!IsEnabled || _disposed) return;
        EvaluateBatteryStatus(suppressInitialAlert: !_initialized);
    }

    private void OnSettingsChanged(object? sender, Settings.AppSettings settings)
    {
        if (settings.EnableBattery != IsEnabled)
        {
            SetEnabled(settings.EnableBattery);
        }
    }

    private void RegisterPowerNotifications()
    {
        if (_ctx is null) return;
        IntPtr hwnd = _ctx.GetWindowHandle();
        if (hwnd == IntPtr.Zero) return;

        try
        {
            _acNotifyHandle = Win32Interop.RegisterPowerSettingNotification(hwnd, ref Win32Interop.GUID_ACDC_POWER_SOURCE, 0);
            _pctNotifyHandle = Win32Interop.RegisterPowerSettingNotification(hwnd, ref Win32Interop.GUID_BATTERY_PERCENTAGE_REMAINING, 0);
        }
        catch
        {
            // Ignore on systems without power setting support
        }
    }

    private void EvaluateBatteryStatus(bool suppressInitialAlert)
    {
        if (!IsEnabled || _ctx is null) return;
        if (!Win32Interop.GetSystemPowerStatus(out var status)) return;

        // 128 = no system battery (desktop PC)
        _hasBattery = (status.BatteryFlag & 128) == 0 && status.BatteryLifePercent <= 100;
        if (!_hasBattery)
        {
            _ctx.ActivityManager.Remove("battery.status");
            _activity = null;
            return;
        }

        int pct = Math.Clamp((int)status.BatteryLifePercent, 0, 100);
        bool isCharging = status.ACLineStatus == 1;
        bool isLow = !isCharging && pct <= 20;

        bool pluggedInNow = isCharging && !_wasCharging;
        _wasCharging = isCharging;
        if (!isLow) _lowAlertSent = false;

        if (isCharging || isLow)
        {
            _activity ??= new BatteryActivity();
            _activity.UpdateState(pct, isCharging, isLow);
            _ctx.ActivityManager.RegisterOrUpdate(_activity);

            if (!suppressInitialAlert && pluggedInNow)
            {
                _ctx.StateMachine.ShowAlert(_activity, TimeSpan.FromSeconds(3.0));
            }
            else if (!suppressInitialAlert && isLow && !_lowAlertSent)
            {
                _lowAlertSent = true;
                _ctx.StateMachine.ShowAlert(_activity, TimeSpan.FromSeconds(4.0));
            }
        }
        else
        {
            _ctx.ActivityManager.Remove("battery.status");
            _activity = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_acNotifyHandle != IntPtr.Zero)
        {
            Win32Interop.UnregisterPowerSettingNotification(_acNotifyHandle);
            _acNotifyHandle = IntPtr.Zero;
        }
        if (_pctNotifyHandle != IntPtr.Zero)
        {
            Win32Interop.UnregisterPowerSettingNotification(_pctNotifyHandle);
            _pctNotifyHandle = IntPtr.Zero;
        }
        if (_ctx is not null)
        {
            _ctx.SettingsService.SettingsChanged -= OnSettingsChanged;
        }
    }
}

