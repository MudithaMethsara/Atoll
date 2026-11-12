using System.Windows;
using System.Windows.Media;
using Atoll.Core.Activities;
using Atoll.Services;
using Windows.Devices.Enumeration;

namespace Atoll.Modules.Bluetooth;

public sealed class BluetoothConnectActivity : IslandActivityBase
{
    public override ActivityKind Kind => ActivityKind.Bluetooth;
    public override Size PreferredAlertSize => new(328, 76);
    public override Size PreferredCompactSize => new(210, 36);

    public int? BatteryPercent { get; }

    internal BluetoothConnectActivity(string deviceName, int? batteryPercent)
        : base($"bt.{deviceName}", "bluetooth")
    {
        BatteryPercent = batteryPercent;
        Title = deviceName;
        Subtitle = batteryPercent.HasValue ? $"Connected · {batteryPercent}% Battery" : "Connected";
        CompactTrailingText = batteryPercent.HasValue ? $"{batteryPercent}%" : "BT";
        Progress = batteryPercent.HasValue ? batteryPercent.Value / 100.0 : null;
        Glyph = "\uE702";
        AccentColor = Color.FromRgb(10, 132, 255);
        Priority = ActivityPriority.High;
    }
}

public sealed class BluetoothModule : IIslandModule
{
    private const string AepConnectedProp = "System.Devices.Aep.IsConnected";
    private const string BluetoothBatteryPnpProp = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string BluetoothAqs = "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\"";

    private ModuleContext? _ctx;
    private DeviceWatcher? _watcher;
    private readonly Dictionary<string, bool> _connectedStates = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialEnumerationComplete;
    private bool _disposed;

    public string ModuleId => "bluetooth";
    public string DisplayName => "Bluetooth Connect Popup";
    public bool IsEnabled { get; private set; }
    public bool IsActive => false;

    public Task InitializeAsync(ModuleContext context)
    {
        _ctx = context;
        IsEnabled = context.SettingsService.Current.EnableBluetooth;
        _ctx.SettingsService.SettingsChanged += OnSettingsChanged;

        if (IsEnabled)
        {
            StartWatcher();
        }
        return Task.CompletedTask;
    }

    public void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled) return;
        IsEnabled = enabled;
        if (enabled) StartWatcher();
        else StopWatcher();
    }

    private void OnSettingsChanged(object? sender, Settings.AppSettings settings)
    {
        if (settings.EnableBluetooth != IsEnabled)
        {
            SetEnabled(settings.EnableBluetooth);
        }
    }

    private void StartWatcher()
    {
        if (_watcher is not null) return;
        try
        {
            string[] props = [AepConnectedProp, BluetoothBatteryPnpProp];
            _watcher = DeviceInformation.CreateWatcher(
                BluetoothAqs,
                props,
                DeviceInformationKind.AssociationEndpoint);

            _watcher.Added += OnDeviceAdded;
            _watcher.Updated += OnDeviceUpdated;
            _watcher.Removed += OnDeviceRemoved;
            _watcher.EnumerationCompleted += (_, _) => _initialEnumerationComplete = true;
            _watcher.Start();
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Warn("Bluetooth", "Bluetooth device watcher unavailable.", ex);
        }
    }

    private void StopWatcher()
    {
        if (_watcher is null) return;
        try
        {
            _watcher.Added -= OnDeviceAdded;
            _watcher.Updated -= OnDeviceUpdated;
            _watcher.Removed -= OnDeviceRemoved;
            if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            {
                _watcher.Stop();
            }
        }
        catch
        {
            // Ignore
        }
        _watcher = null;
        _initialEnumerationComplete = false;
        _connectedStates.Clear();
    }

    private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation args)
    {
        bool connected = TryGetBool(args.Properties, AepConnectedProp);
        _connectedStates[args.Id] = connected;
        if (_initialEnumerationComplete && connected && !string.IsNullOrWhiteSpace(args.Name))
        {
            int? battery = TryGetBattery(args.Properties);
            DispatchConnectAlert(args.Name, battery);
        }
    }

    private void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        if (!args.Properties.ContainsKey(AepConnectedProp)) return;

        bool wasConnected = _connectedStates.TryGetValue(args.Id, out bool prev) && prev;
        bool nowConnected = TryGetBool(args.Properties, AepConnectedProp);
        _connectedStates[args.Id] = nowConnected;

        if (_initialEnumerationComplete && !wasConnected && nowConnected)
        {
            _ = FetchAndShowDeviceAsync(args.Id);
        }
    }

    private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        _connectedStates.Remove(args.Id);
    }

    private async Task FetchAndShowDeviceAsync(string id)
    {
        if (!IsEnabled || _disposed) return;
        try
        {
            string[] props = [AepConnectedProp, BluetoothBatteryPnpProp];
            var info = await DeviceInformation.CreateFromIdAsync(id, props, DeviceInformationKind.AssociationEndpoint);
            if (info is null || string.IsNullOrWhiteSpace(info.Name)) return;

            int? battery = TryGetBattery(info.Properties);
            DispatchConnectAlert(info.Name, battery);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Debug("Bluetooth", $"Device detail lookup failed: {ex.Message}");
        }
    }

    private void DispatchConnectAlert(string name, int? battery)
    {
        if (!IsEnabled || _disposed || _ctx is null) return;
        _ctx.UiDispatcher.BeginInvoke(() =>
        {
            if (!IsEnabled || _disposed) return;
            var alert = new BluetoothConnectActivity(name, battery);
            _ctx.StateMachine.ShowAlert(alert, TimeSpan.FromSeconds(3.5));
        });
    }

    private static bool TryGetBool(IReadOnlyDictionary<string, object> props, string key)
    {
        return props.TryGetValue(key, out object? val) && val is bool b && b;
    }

    private static int? TryGetBattery(IReadOnlyDictionary<string, object> props)
    {
        if (props.TryGetValue(BluetoothBatteryPnpProp, out object? pnpVal) && pnpVal is byte b1)
            return Math.Clamp((int)b1, 0, 100);
        if (props.TryGetValue(BluetoothBatteryPnpProp, out object? intVal) && intVal is int i)
            return Math.Clamp(i, 0, 100);
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopWatcher();
        if (_ctx is not null)
        {
            _ctx.SettingsService.SettingsChanged -= OnSettingsChanged;
        }
    }
}

