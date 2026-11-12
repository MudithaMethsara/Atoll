using System.Windows;
using System.Windows.Media;
using Atoll.Core.Activities;
using Atoll.Core.Window;

namespace Atoll.Modules.Osd;

public enum OsdKind
{
    Volume,
    Brightness
}

public sealed class OsdActivity : IslandActivityBase
{
    private readonly OsdModule _owner;
    private double _valuePercent;
    private bool _isMuted;
    private OsdKind _osdKind;

    public override ActivityKind Kind => ActivityKind.Osd;
    public override bool SupportsMinimal => false;
    public override Size PreferredAlertSize => new(316, 68);
    public override Size PreferredExpandedSize => new(340, 96);

    public OsdKind OsdKind
    {
        get => _osdKind;
        internal set => SetField(ref _osdKind, value);
    }

    public double ValuePercent
    {
        get => _valuePercent;
        internal set
        {
            if (SetField(ref _valuePercent, Math.Clamp(value, 0, 100)))
            {
                Progress = _valuePercent / 100.0;
                Subtitle = IsMuted && OsdKind == OsdKind.Volume ? "Muted" : $"{Math.Round(_valuePercent)}%";
                RaisePropertyChanged(nameof(ValueText));
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        internal set
        {
            if (SetField(ref _isMuted, value))
            {
                UpdateGlyph();
                RaisePropertyChanged(nameof(ValueText));
            }
        }
    }

    public string ValueText => IsMuted && OsdKind == OsdKind.Volume ? "Muted" : $"{Math.Round(ValuePercent)}%";

    internal OsdActivity(OsdModule owner) : base("osd.hud", "osd")
    {
        _owner = owner;
        Priority = ActivityPriority.Critical;
    }

    internal void Update(OsdKind kind, double percent, bool isMuted)
    {
        OsdKind = kind;
        IsMuted = isMuted;
        ValuePercent = percent;
        Title = kind == OsdKind.Volume ? "Volume" : "Brightness";
        AccentColor = kind == OsdKind.Volume
            ? Color.FromRgb(10, 132, 255)
            : Color.FromRgb(255, 214, 10);
        UpdateGlyph();
    }

    public void SetInteractiveValue(double percent) => _owner.ApplyInteractiveValue(OsdKind, percent);

    private void UpdateGlyph()
    {
        if (OsdKind == OsdKind.Brightness)
        {
            Glyph = "\uE706";
            return;
        }

        if (IsMuted || ValuePercent <= 0.5) Glyph = "\uE74F";
        else if (ValuePercent < 35) Glyph = "\uE993";
        else if (ValuePercent < 70) Glyph = "\uE994";
        else Glyph = "\uE995";
    }
}

public sealed class OsdModule : IIslandModule
{
    private ModuleContext? _ctx;
    private OsdActivity? _activity;
    private bool _suppressEcho;
    private bool _disposed;

    public string ModuleId => "osd";
    public string DisplayName => "Volume & Brightness HUD";
    public bool IsEnabled { get; private set; }
    public bool IsActive => false;

    public Task InitializeAsync(ModuleContext context)
    {
        _ctx = context;
        IsEnabled = context.SettingsService.Current.EnableOsd;
        _activity = new OsdActivity(this);

        _ctx.AudioService.VolumeChanged += OnVolumeChanged;
        _ctx.BrightnessService.BrightnessChanged += OnBrightnessChanged;
        _ctx.SettingsService.SettingsChanged += OnSettingsChanged;
        return Task.CompletedTask;
    }

    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
    }

    private void OnSettingsChanged(object? sender, Settings.AppSettings settings)
    {
        if (settings.EnableOsd != IsEnabled)
        {
            SetEnabled(settings.EnableOsd);
        }
    }

    private void OnVolumeChanged(object? sender, (float VolumePercent, bool IsMuted) data)
    {
        if (!IsEnabled || _disposed || _ctx is null || _suppressEcho) return;
        _ctx.UiDispatcher.BeginInvoke(() => ShowOsd(OsdKind.Volume, data.VolumePercent, data.IsMuted));
    }

    private void OnBrightnessChanged(object? sender, int percent)
    {
        if (!IsEnabled || _disposed || _ctx is null || _suppressEcho) return;
        _ctx.UiDispatcher.BeginInvoke(() => ShowOsd(OsdKind.Brightness, percent, isMuted: false));
    }

    public void ShowVolumeHud(double volumePercent, bool isMuted)
    {
        ShowOsd(OsdKind.Volume, volumePercent, isMuted);
    }

    private void ShowOsd(OsdKind kind, double percent, bool isMuted)
    {
        if (!IsEnabled || _ctx is null || _activity is null) return;

        if (_ctx.SettingsService.Current.SuppressLegacyOsd)
        {
            TryHideLegacyWindows10Osd();
        }

        _activity.Update(kind, percent, isMuted);
        _ctx.StateMachine.ShowAlert(_activity, TimeSpan.FromSeconds(2.1));
    }

    internal void ApplyInteractiveValue(OsdKind kind, double percent)
    {
        if (_ctx is null) return;
        _suppressEcho = true;
        try
        {
            if (kind == OsdKind.Volume)
            {
                _ctx.AudioService.SetVolumePercent(percent);
                _activity?.Update(OsdKind.Volume, percent, isMuted: false);
            }
            else
            {
                _ctx.BrightnessService.SetBrightness((int)Math.Round(percent));
                _activity?.Update(OsdKind.Brightness, percent, isMuted: false);
            }
            if (_activity is not null)
            {
                _ctx.StateMachine.ShowAlert(_activity, TimeSpan.FromSeconds(2.4));
            }
        }
        finally
        {
            _suppressEcho = false;
        }
    }

    private static void TryHideLegacyWindows10Osd()
    {
        try
        {
            // Legacy Win10 top-left volume/brightness HUD window class
            IntPtr hwnd = Win32Interop.FindWindow("NativeHWNDHost", "");
            if (hwnd != IntPtr.Zero)
            {
                Win32Interop.ShowWindow(hwnd, 0); // SW_HIDE
            }
        }
        catch
        {
            // Graceful fallback on Windows 11
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ctx is not null)
        {
            _ctx.AudioService.VolumeChanged -= OnVolumeChanged;
            _ctx.BrightnessService.BrightnessChanged -= OnBrightnessChanged;
            _ctx.SettingsService.SettingsChanged -= OnSettingsChanged;
        }
    }
}

