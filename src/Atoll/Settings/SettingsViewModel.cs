using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Atoll.Core.Window;
using Atoll.Services;

namespace Atoll.Settings;

public sealed record MonitorOption(string DeviceName, string DisplayLabel);

public sealed record AccentPreset(string Name, string Hex);

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _settingsService;
    private readonly StartupService _startupService;
    private readonly Action? _toggleIslandAction;
    private readonly Action? _previewAlertAction;
    private AppSettings _draft;

    public IReadOnlyList<MonitorOption> Monitors { get; }

    public IReadOnlyList<AccentPreset> AccentPresets { get; } =
    [
        new AccentPreset("Blue", "#0A84FF"),
        new AccentPreset("Mint", "#30D158"),
        new AccentPreset("Violet", "#BF5AF2"),
        new AccentPreset("Amber", "#FF9F0A"),
        new AccentPreset("Coral", "#FF453A"),
        new AccentPreset("Cyan", "#64D2FF")
    ];

    public string AppVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public string MonitorDeviceName
    {
        get => _draft.MonitorDeviceName ?? string.Empty;
        set
        {
            string normalized = value ?? string.Empty;
            if (!string.Equals(_draft.MonitorDeviceName, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _draft.MonitorDeviceName = normalized;
                Commit();
            }
        }
    }

    public double TopOffset
    {
        get => _draft.TopOffset;
        set { if (Math.Abs(_draft.TopOffset - value) > 0.01) { _draft.TopOffset = value; Commit(); } }
    }

    public double HorizontalOffset
    {
        get => _draft.HorizontalOffset;
        set { if (Math.Abs(_draft.HorizontalOffset - value) > 0.01) { _draft.HorizontalOffset = value; Commit(); } }
    }

    public bool NotchMode
    {
        get => _draft.NotchMode;
        set { if (_draft.NotchMode != value) { _draft.NotchMode = value; Commit(); } }
    }

    public double SizeScale
    {
        get => _draft.SizeScale;
        set { if (Math.Abs(_draft.SizeScale - value) > 0.005) { _draft.SizeScale = value; Commit(); } }
    }

    public double AnimationSpeed
    {
        get => _draft.AnimationSpeed;
        set { if (Math.Abs(_draft.AnimationSpeed - value) > 0.005) { _draft.AnimationSpeed = value; Commit(); } }
    }

    public bool LowPowerMode
    {
        get => _draft.LowPowerMode;
        set { if (_draft.LowPowerMode != value) { _draft.LowPowerMode = value; Commit(); } }
    }

    public bool AutoHideFullscreen
    {
        get => _draft.AutoHideFullscreen;
        set { if (_draft.AutoHideFullscreen != value) { _draft.AutoHideFullscreen = value; Commit(); } }
    }

    public bool ShrinkInsteadOfHideInFullscreen
    {
        get => _draft.ShrinkInsteadOfHideInFullscreen;
        set { if (_draft.ShrinkInsteadOfHideInFullscreen != value) { _draft.ShrinkInsteadOfHideInFullscreen = value; Commit(); } }
    }

    public bool HoverExpand
    {
        get => _draft.HoverExpand;
        set { if (_draft.HoverExpand != value) { _draft.HoverExpand = value; Commit(); } }
    }

    public string AccentColorHex
    {
        get => _draft.AccentColorHex;
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (!trimmed.StartsWith('#') && trimmed.Length == 6) trimmed = "#" + trimmed;
            if (!string.Equals(_draft.AccentColorHex, trimmed, StringComparison.OrdinalIgnoreCase) && trimmed.Length >= 4)
            {
                _draft.AccentColorHex = trimmed.ToUpperInvariant();
                Commit();
            }
        }
    }

    public string ToggleExpandHotkey
    {
        get => _draft.ToggleExpandHotkey;
        set { if (_draft.ToggleExpandHotkey != value) { _draft.ToggleExpandHotkey = value; Commit(); } }
    }

    public bool StartWithWindows
    {
        get => _draft.StartWithWindows;
        set
        {
            if (_draft.StartWithWindows != value)
            {
                _draft.StartWithWindows = value;
                _startupService.SetStartupEnabled(value);
                Commit();
            }
        }
    }

    public bool SuppressLegacyOsd
    {
        get => _draft.SuppressLegacyOsd;
        set { if (_draft.SuppressLegacyOsd != value) { _draft.SuppressLegacyOsd = value; Commit(); } }
    }

    public bool DiagnosticsVisible
    {
        get => _draft.DiagnosticsVisible;
        set { if (_draft.DiagnosticsVisible != value) { _draft.DiagnosticsVisible = value; Commit(); } }
    }

    public bool EnableMedia { get => _draft.EnableMedia; set { _draft.EnableMedia = value; Commit(); } }
    public bool EnableTimer { get => _draft.EnableTimer; set { _draft.EnableTimer = value; Commit(); } }
    public bool EnableCalls { get => _draft.EnableCalls; set { _draft.EnableCalls = value; Commit(); } }
    public bool EnableNotifications { get => _draft.EnableNotifications; set { _draft.EnableNotifications = value; Commit(); } }
    public bool EnableBattery { get => _draft.EnableBattery; set { _draft.EnableBattery = value; Commit(); } }
    public bool EnableBluetooth { get => _draft.EnableBluetooth; set { _draft.EnableBluetooth = value; Commit(); } }
    public bool EnableOsd { get => _draft.EnableOsd; set { _draft.EnableOsd = value; Commit(); } }
    public bool EnablePrivacy { get => _draft.EnablePrivacy; set { _draft.EnablePrivacy = value; Commit(); } }
    public bool EnableCalendar { get => _draft.EnableCalendar; set { _draft.EnableCalendar = value; Commit(); } }
    public bool EnableFileShelf { get => _draft.EnableFileShelf; set { _draft.EnableFileShelf = value; Commit(); } }
    public bool EnableQuickActions { get => _draft.EnableQuickActions; set { _draft.EnableQuickActions = value; Commit(); } }
    public bool EnableWeatherGlance { get => _draft.EnableWeatherGlance; set { _draft.EnableWeatherGlance = value; Commit(); } }

    public string WeatherLocationQuery
    {
        get => _draft.WeatherLocationQuery;
        set { if (_draft.WeatherLocationQuery != value) { _draft.WeatherLocationQuery = value; Commit(); } }
    }

    public bool UseCelsius
    {
        get => _draft.UseCelsius;
        set { if (_draft.UseCelsius != value) { _draft.UseCelsius = value; Commit(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsViewModel(
        SettingsService settingsService,
        DisplayMonitorService monitorService,
        StartupService startupService,
        Action? toggleIslandAction = null,
        Action? previewAlertAction = null)
    {
        _settingsService = settingsService;
        _startupService = startupService;
        _toggleIslandAction = toggleIslandAction;
        _previewAlertAction = previewAlertAction;
        _draft = settingsService.Current.Clone();

        var options = new List<MonitorOption>
        {
            new MonitorOption(string.Empty, "Primary Monitor (Auto)")
        };

        int index = 1;
        foreach (var monitor in monitorService.GetMonitors())
        {
            string res = $"{(int)monitor.PhysicalBounds.Width}×{(int)monitor.PhysicalBounds.Height}";
            string primaryTag = monitor.IsPrimary ? " · Primary" : string.Empty;
            string cleanName = monitor.DeviceName.Replace(@"\\.\", string.Empty);
            options.Add(new MonitorOption(monitor.DeviceName, $"Display {index++} — {cleanName} ({res}{primaryTag})"));
        }

        Monitors = options;
    }

    public string LogDirectory =>
        System.IO.Path.GetDirectoryName(LoggingService.Instance.LogFilePath)
        ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public void ToggleIslandPreview() => _toggleIslandAction?.Invoke();

    public void TriggerSampleAlert() => _previewAlertAction?.Invoke();

    public void ResetDefaults()
    {
        _draft = new AppSettings();
        _startupService.SetStartupEnabled(_draft.StartWithWindows);
        _settingsService.Replace(_draft);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private void Commit([CallerMemberName] string? propertyName = null)
    {
        _settingsService.Replace(_draft);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

