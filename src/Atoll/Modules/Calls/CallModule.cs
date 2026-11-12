using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Atoll.Core.Activities;
using Atoll.Core.Window;
using Atoll.Services;

namespace Atoll.Modules.Calls;

public sealed class CallActivity : IslandActivityBase
{
    private readonly CallModule _owner;
    private bool _isMuted;
    private string _durationText = "00:00";
    private int _processId;

    public override Atoll.Core.Activities.ActivityKind Kind => Atoll.Core.Activities.ActivityKind.Call;
    public override Size PreferredCompactSize => new(226, 36);
    public override Size PreferredExpandedSize => new(372, 146);

    public bool IsMuted
    {
        get => _isMuted;
        internal set
        {
            if (SetField(ref _isMuted, value))
            {
                RaisePropertyChanged(nameof(MuteGlyph));
                RaisePropertyChanged(nameof(MuteLabel));
            }
        }
    }

    public string MuteGlyph => IsMuted ? "\uF781" : "\uE720";
    public string MuteLabel => IsMuted ? "Unmute" : "Mute";

    public string DurationText
    {
        get => _durationText;
        internal set
        {
            if (SetField(ref _durationText, value))
            {
                CompactTrailingText = value;
                Subtitle = value;
            }
        }
    }

    internal int ProcessId
    {
        get => _processId;
        set => _processId = value;
    }

    internal CallActivity(CallModule owner) : base("calls.active", "calls")
    {
        _owner = owner;
        Priority = ActivityPriority.Critical;
        AccentColor = Color.FromRgb(48, 209, 88);
        Glyph = "\uE717";
    }

    internal void SetCallDetails(string appTitle, string subtitle, int pid, bool isMuted)
    {
        Title = appTitle;
        Subtitle = subtitle;
        ProcessId = pid;
        IsMuted = isMuted;
    }

    public void ToggleMute() => _owner.ToggleCallMute();
    public void HangUpOrFocus() => _owner.FocusOrEndCall();
}

public sealed class CallModule : IIslandModule
{
    private static readonly string[] CallProcessNames =
    [
        "ms-teams",
        "Teams",
        "Zoom",
        "Discord",
        "slack",
        "CiscoCollabHost"
    ];

    private ModuleContext? _ctx;
    private CallActivity? _activity;
    private DispatcherTimer? _durationTimer;
    private DispatcherTimer? _recheckTimer;
    private DateTime _callStartUtc;
    private int _activeCallPid;
    private bool _disposed;

    public string ModuleId => "calls";
    public string DisplayName => "Call & Meeting Indicator";
    public bool IsEnabled { get; private set; }
    public bool IsActive => _activity is not null && IsEnabled;

    public Task InitializeAsync(ModuleContext context)
    {
        _ctx = context;
        IsEnabled = context.SettingsService.Current.EnableCalls;
        _ctx.AudioService.AudioSessionsMightHaveChanged += OnAudioSessionsChanged;
        _ctx.SettingsService.SettingsChanged += OnSettingsChanged;

        if (IsEnabled)
        {
            EvaluateCallState();
        }
        return Task.CompletedTask;
    }

    public void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled || _ctx is null) return;
        IsEnabled = enabled;

        if (enabled)
        {
            EvaluateCallState();
        }
        else
        {
            ClearCall();
        }
    }

    private void OnSettingsChanged(object? sender, Settings.AppSettings settings)
    {
        if (settings.EnableCalls != IsEnabled)
        {
            SetEnabled(settings.EnableCalls);
        }
    }

    private void OnAudioSessionsChanged(object? sender, EventArgs e)
    {
        _ctx?.UiDispatcher.BeginInvoke(EvaluateCallState);
    }

    public void EvaluateCallState()
    {
        if (!IsEnabled || _disposed || _ctx is null) return;

        try
        {
            var sessions = _ctx.AudioService.GetActiveCommunicationSessions();
            ActiveAudioSessionInfo? matched = sessions.FirstOrDefault(s =>
                CallProcessNames.Any(p => s.ProcessName.Contains(p, StringComparison.OrdinalIgnoreCase)));

            if (matched is null)
            {
                // Fallback: detect active meeting window titles for Zoom/Teams/Discord when comms role isn't flagged
                var fgMatch = FindMeetingProcessByWindowTitle();
                if (fgMatch is null)
                {
                    ClearCall();
                    return;
                }

                ActivateOrUpdateCall(fgMatch.Value.Pid, fgMatch.Value.Label, isMuted: false);
                return;
            }

            string label = FormatCallAppName(matched.ProcessName);
            ActivateOrUpdateCall(matched.ProcessId, label, matched.IsMuted);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Debug("Calls", $"Call detection check failed: {ex.Message}");
        }
    }

    private void ActivateOrUpdateCall(int pid, string appName, bool isMuted)
    {
        if (_ctx is null) return;

        if (_activity is null || _activeCallPid != pid)
        {
            _activeCallPid = pid;
            _callStartUtc = DateTime.UtcNow;
            _activity = new CallActivity(this);
        }

        _activity.SetCallDetails(appName, _activity.DurationText, pid, isMuted);
        _ctx.ActivityManager.RegisterOrUpdate(_activity);
        EnsureActiveCallTimers();
    }

    private void ClearCall()
    {
        StopActiveCallTimers();
        _activeCallPid = 0;
        if (_activity is not null)
        {
            _ctx?.ActivityManager.Remove("calls.active");
            _activity = null;
        }
    }

    private void EnsureActiveCallTimers()
    {
        if (_ctx is null) return;

        if (_durationTimer is null)
        {
            _durationTimer = new DispatcherTimer(DispatcherPriority.Normal, _ctx.UiDispatcher)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _durationTimer.Tick += (_, _) =>
            {
                if (_activity is null) return;
                var elapsed = DateTime.UtcNow - _callStartUtc;
                _activity.DurationText = elapsed.TotalHours >= 1
                    ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
                    : $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            };
            _durationTimer.Start();
        }

        // Only recheck session liveness while a call is actually active
        if (_recheckTimer is null)
        {
            _recheckTimer = new DispatcherTimer(DispatcherPriority.Background, _ctx.UiDispatcher)
            {
                Interval = TimeSpan.FromSeconds(4)
            };
            _recheckTimer.Tick += (_, _) => EvaluateCallState();
            _recheckTimer.Start();
        }
    }

    private void StopActiveCallTimers()
    {
        _durationTimer?.Stop();
        _durationTimer = null;
        _recheckTimer?.Stop();
        _recheckTimer = null;
    }

    public void ToggleCallMute()
    {
        if (_ctx is null || _activity is null || _activeCallPid == 0) return;
        if (_ctx.AudioService.TryToggleSessionMute(_activeCallPid))
        {
            _activity.IsMuted = !_activity.IsMuted;
        }
    }

    public void FocusOrEndCall()
    {
        if (_activeCallPid == 0) return;
        try
        {
            using var proc = Process.GetProcessById(_activeCallPid);
            if (proc.MainWindowHandle != IntPtr.Zero)
            {
                Win32Interop.ShowWindow(proc.MainWindowHandle, 9); // SW_RESTORE
                Win32Interop.SetForegroundWindow(proc.MainWindowHandle);
            }
        }
        catch
        {
            ClearCall();
        }
    }

    private static (int Pid, string Label)? FindMeetingProcessByWindowTitle()
    {
        IntPtr fg = Win32Interop.GetForegroundWindow();
        if (fg == IntPtr.Zero) return null;

        Win32Interop.GetWindowThreadProcessId(fg, out uint pid);
        if (pid == 0) return null;

        try
        {
            using var proc = Process.GetProcessById((int)pid);
            if (!CallProcessNames.Any(p => proc.ProcessName.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            var sb = new StringBuilder(256);
            Win32Interop.GetWindowText(fg, sb, sb.Capacity);
            string title = sb.ToString();
            if (title.Contains("Meeting", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Call", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Voice", StringComparison.OrdinalIgnoreCase))
            {
                return ((int)pid, FormatCallAppName(proc.ProcessName));
            }
        }
        catch
        {
            // Ignore transient process exits
        }

        return null;
    }

    private static string FormatCallAppName(string processName)
    {
        if (processName.Contains("teams", StringComparison.OrdinalIgnoreCase)) return "Microsoft Teams";
        if (processName.Contains("zoom", StringComparison.OrdinalIgnoreCase)) return "Zoom Meeting";
        if (processName.Contains("discord", StringComparison.OrdinalIgnoreCase)) return "Discord Call";
        if (processName.Contains("slack", StringComparison.OrdinalIgnoreCase)) return "Slack Huddle";
        return "Active Call";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopActiveCallTimers();
        if (_ctx is not null)
        {
            _ctx.AudioService.AudioSessionsMightHaveChanged -= OnAudioSessionsChanged;
            _ctx.SettingsService.SettingsChanged -= OnSettingsChanged;
        }
    }
}
