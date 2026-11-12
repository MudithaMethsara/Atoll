using System.Text.Json.Serialization;

namespace Atoll.Settings;

public sealed class AppSettings
{
    public string MonitorDeviceName { get; set; } = string.Empty;
    public double TopOffset { get; set; } = 8.0;
    public double HorizontalOffset { get; set; } = 0.0;
    public bool NotchMode { get; set; } = false;
    public double SizeScale { get; set; } = 1.0;

    public double AnimationSpeed { get; set; } = 1.0;
    public bool LowPowerMode { get; set; } = false;
    public bool AutoHideFullscreen { get; set; } = true;
    public bool ShrinkInsteadOfHideInFullscreen { get; set; } = false;
    public bool HoverExpand { get; set; } = false;

    public string AccentColorHex { get; set; } = "#0A84FF";
    public bool StartWithWindows { get; set; } = false;
    public bool SuppressLegacyOsd { get; set; } = true;
    public bool DiagnosticsVisible { get; set; } = false;
    public string ToggleExpandHotkey { get; set; } = "Ctrl+Alt+Space";

    // Per-feature toggles
    public bool EnableMedia { get; set; } = true;
    public bool EnableTimer { get; set; } = true;
    public bool EnableCalls { get; set; } = true;
    public bool EnableNotifications { get; set; } = true;
    public bool EnableBattery { get; set; } = true;
    public bool EnableBluetooth { get; set; } = true;
    public bool EnableOsd { get; set; } = true;
    public bool EnablePrivacy { get; set; } = true;
    public bool EnableCalendar { get; set; } = true;
    public bool EnableFileShelf { get; set; } = true;
    public bool EnableQuickActions { get; set; } = true;

    // Optional glance mode (off by default per spec)
    public bool EnableWeatherGlance { get; set; } = false;
    public string WeatherLocationQuery { get; set; } = string.Empty;
    public bool UseCelsius { get; set; } = true;

    public AppSettings Clone() => new()
    {
        MonitorDeviceName = MonitorDeviceName,
        TopOffset = TopOffset,
        HorizontalOffset = HorizontalOffset,
        NotchMode = NotchMode,
        SizeScale = SizeScale,
        AnimationSpeed = AnimationSpeed,
        LowPowerMode = LowPowerMode,
        AutoHideFullscreen = AutoHideFullscreen,
        ShrinkInsteadOfHideInFullscreen = ShrinkInsteadOfHideInFullscreen,
        HoverExpand = HoverExpand,
        AccentColorHex = AccentColorHex,
        StartWithWindows = StartWithWindows,
        SuppressLegacyOsd = SuppressLegacyOsd,
        DiagnosticsVisible = DiagnosticsVisible,
        ToggleExpandHotkey = ToggleExpandHotkey,
        EnableMedia = EnableMedia,
        EnableTimer = EnableTimer,
        EnableCalls = EnableCalls,
        EnableNotifications = EnableNotifications,
        EnableBattery = EnableBattery,
        EnableBluetooth = EnableBluetooth,
        EnableOsd = EnableOsd,
        EnablePrivacy = EnablePrivacy,
        EnableCalendar = EnableCalendar,
        EnableFileShelf = EnableFileShelf,
        EnableQuickActions = EnableQuickActions,
        EnableWeatherGlance = EnableWeatherGlance,
        WeatherLocationQuery = WeatherLocationQuery,
        UseCelsius = UseCelsius
    };
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}

