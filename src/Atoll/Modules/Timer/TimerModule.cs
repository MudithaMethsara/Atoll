using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Atoll.Core.Activities;

namespace Atoll.Modules.Timer;

public sealed class TimerActivity : IslandActivityBase
{
    private readonly TimerModule _owner;
    private bool _isStopwatch;
    private bool _isRunning;
    private string _formattedTime = "00:00";

    public override Atoll.Core.Activities.ActivityKind Kind => Atoll.Core.Activities.ActivityKind.Timer;
    public override Size PreferredCompactSize => new(206, 36);
    public override Size PreferredExpandedSize => new(364, 152);
    public override Size PreferredAlertSize => new(320, 74);

    public bool IsStopwatch
    {
        get => _isStopwatch;
        internal set => SetField(ref _isStopwatch, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        internal set
        {
            if (SetField(ref _isRunning, value))
            {
                RaisePropertyChanged(nameof(PlayPauseGlyph));
            }
        }
    }

    public string PlayPauseGlyph => IsRunning ? "\uE769" : "\uE768";

    public string FormattedTime
    {
        get => _formattedTime;
        internal set
        {
            if (SetField(ref _formattedTime, value))
            {
                CompactTrailingText = value;
                Subtitle = value;
            }
        }
    }

    internal TimerActivity(TimerModule owner) : base("timer.active", "timer")
    {
        _owner = owner;
        Priority = ActivityPriority.High;
        AccentColor = Color.FromRgb(255, 159, 10);
        Glyph = "\uE916";
        Title = "Timer";
    }

    internal void SetHeader(string label, string glyph)
    {
        Title = label;
        Glyph = glyph;
    }

    internal void SetProgress(double value) => Progress = value;

    public void StartPreset(int minutes) => _owner.StartCountdown(TimeSpan.FromMinutes(minutes), $"{minutes}m Timer");
    public void StartStopwatch() => _owner.StartStopwatch();
    public void TogglePause() => _owner.TogglePause();
    public void StopAndClear() => _owner.Stop();
}

public sealed class TimerModule : IIslandModule
{
    private ModuleContext? _ctx;
    private TimerActivity? _activity;
    private DispatcherTimer? _tickTimer;
    private readonly Stopwatch _stopwatch = new();
    private TimeSpan _targetDuration;
    private TimeSpan _accumulatedElapsed;
    private bool _isStopwatchMode;
    private bool _isRunning;
    private bool _disposed;

    public string ModuleId => "timer";
    public string DisplayName => "Timers & Stopwatch";
    public bool IsEnabled { get; private set; }
    public bool IsActive => _activity is not null && IsEnabled;

    public Task InitializeAsync(ModuleContext context)
    {
        _ctx = context;
        IsEnabled = context.SettingsService.Current.EnableTimer;
        _ctx.SettingsService.SettingsChanged += OnSettingsChanged;
        return Task.CompletedTask;
    }

    public void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled) return;
        IsEnabled = enabled;
        if (!enabled) Stop();
    }

    private void OnSettingsChanged(object? sender, Settings.AppSettings settings)
    {
        if (settings.EnableTimer != IsEnabled)
        {
            SetEnabled(settings.EnableTimer);
        }
    }

    public void StartCountdown(TimeSpan duration, string label = "Timer")
    {
        if (!IsEnabled || _ctx is null || duration <= TimeSpan.Zero) return;

        _isStopwatchMode = false;
        _targetDuration = duration;
        _accumulatedElapsed = TimeSpan.Zero;
        _stopwatch.Restart();
        _isRunning = true;

        EnsureActivity(label, isStopwatch: false);
        UpdateDisplay();
        EnsureTickTimer();
        _ctx.ActivityManager.RegisterOrUpdate(_activity!);
    }

    public void StartStopwatch()
    {
        if (!IsEnabled || _ctx is null) return;

        _isStopwatchMode = true;
        _targetDuration = TimeSpan.Zero;
        _accumulatedElapsed = TimeSpan.Zero;
        _stopwatch.Restart();
        _isRunning = true;

        EnsureActivity("Stopwatch", isStopwatch: true);
        UpdateDisplay();
        EnsureTickTimer();
        _ctx.ActivityManager.RegisterOrUpdate(_activity!);
    }

    public void TogglePause()
    {
        if (_activity is null) return;

        if (_isRunning)
        {
            _accumulatedElapsed += _stopwatch.Elapsed;
            _stopwatch.Reset();
            _isRunning = false;
            StopTickTimer();
        }
        else
        {
            _stopwatch.Restart();
            _isRunning = true;
            EnsureTickTimer();
        }

        _activity.IsRunning = _isRunning;
        UpdateDisplay();
    }

    public void Stop()
    {
        _isRunning = false;
        _stopwatch.Reset();
        _accumulatedElapsed = TimeSpan.Zero;
        StopTickTimer();
        _ctx?.ActivityManager.Remove("timer.active");
        _activity = null;
    }

    private void EnsureActivity(string label, bool isStopwatch)
    {
        _activity ??= new TimerActivity(this);
        _activity.IsStopwatch = isStopwatch;
        _activity.IsRunning = _isRunning;
        _activity.SetHeader(label, isStopwatch ? "\uE917" : "\uE916");
    }

    private void EnsureTickTimer()
    {
        if (_ctx is null || _tickTimer is not null) return;
        _tickTimer = new DispatcherTimer(DispatcherPriority.Normal, _ctx.UiDispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _tickTimer.Tick += (_, _) => UpdateDisplay();
        _tickTimer.Start();
    }

    private void StopTickTimer()
    {
        if (_tickTimer is null) return;
        _tickTimer.Stop();
        _tickTimer = null;
    }

    private void UpdateDisplay()
    {
        if (_activity is null || _ctx is null) return;

        TimeSpan elapsed = _accumulatedElapsed + (_isRunning ? _stopwatch.Elapsed : TimeSpan.Zero);

        if (_isStopwatchMode)
        {
            _activity.SetProgress((elapsed.TotalSeconds % 60.0) / 60.0);
            _activity.FormattedTime = FormatDuration(elapsed);
            return;
        }

        TimeSpan remaining = _targetDuration - elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            _isRunning = false;
            _stopwatch.Reset();
            StopTickTimer();
            _activity.IsRunning = false;
            _activity.SetProgress(0.0);
            _activity.FormattedTime = "00:00";

            try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
            _ctx.StateMachine.ShowAlert(_activity, TimeSpan.FromSeconds(5));
            return;
        }

        _activity.SetProgress(Math.Clamp(remaining.TotalSeconds / _targetDuration.TotalSeconds, 0.0, 1.0));
        _activity.FormattedTime = FormatDuration(remaining);
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
        int totalSeconds = (int)Math.Ceiling(ts.TotalSeconds);
        int hours = totalSeconds / 3600;
        int minutes = (totalSeconds % 3600) / 60;
        int seconds = totalSeconds % 60;
        return hours > 0 ? $"{hours}:{minutes:D2}:{seconds:D2}" : $"{minutes:D2}:{seconds:D2}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopTickTimer();
        if (_ctx is not null)
        {
            _ctx.SettingsService.SettingsChanged -= OnSettingsChanged;
        }
    }
}
