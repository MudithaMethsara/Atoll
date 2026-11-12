using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Atoll.Core.Activities;
using Atoll.Services;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Atoll.Modules.Media;

public sealed class MediaActivity : IslandActivityBase
{
    private readonly MediaModule _owner;
    private bool _isPlaying;
    private TimeSpan _position;
    private TimeSpan _duration;
    private string _sourceApp = string.Empty;
    private float[] _spectrum = [0.15f, 0.25f, 0.2f, 0.3f, 0.15f];

    public override ActivityKind Kind => ActivityKind.Media;
    public override Size PreferredCompactSize => new(236, 36);
    public override Size PreferredExpandedSize => new(396, 178);

    public bool IsPlaying
    {
        get => _isPlaying;
        internal set
        {
            if (SetField(ref _isPlaying, value))
            {
                RaisePropertyChanged(nameof(PlayPauseGlyph));
            }
        }
    }

    public string PlayPauseGlyph => IsPlaying ? "\uE769" : "\uE768";

    public TimeSpan Position
    {
        get => _position;
        internal set
        {
            if (SetField(ref _position, value))
            {
                RaisePropertyChanged(nameof(PositionText));
                RaisePropertyChanged(nameof(ProgressRatio));
            }
        }
    }

    public TimeSpan Duration
    {
        get => _duration;
        internal set
        {
            if (SetField(ref _duration, value))
            {
                RaisePropertyChanged(nameof(DurationText));
                RaisePropertyChanged(nameof(ProgressRatio));
            }
        }
    }

    public string SourceApp
    {
        get => _sourceApp;
        internal set => SetField(ref _sourceApp, value);
    }

    public float[] Spectrum
    {
        get => _spectrum;
        internal set => SetField(ref _spectrum, value);
    }

    public string PositionText => FormatTime(Position);
    public string DurationText => FormatTime(Duration);
    public double ProgressRatio => Duration.TotalSeconds > 0.5 ? Math.Clamp(Position.TotalSeconds / Duration.TotalSeconds, 0.0, 1.0) : 0.0;

    internal MediaActivity(MediaModule owner) : base("media.current", "media")
    {
        _owner = owner;
        Priority = ActivityPriority.Normal;
        Glyph = "\uE8D6";
    }

    internal void UpdateMetadata(string title, string artist, string sourceApp, ImageSource? art, Color dominant)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "Playing Media" : title;
        Subtitle = string.IsNullOrWhiteSpace(artist) ? sourceApp : artist;
        SourceApp = sourceApp;
        Thumbnail = art;
        AccentColor = dominant;
    }

    public void TogglePlayPause() => _ = _owner.TogglePlayPauseAsync();
    public void NextTrack() => _ = _owner.NextTrackAsync();
    public void PreviousTrack() => _ = _owner.PreviousTrackAsync();
    public void SeekRatio(double ratio) => _ = _owner.SeekRatioAsync(ratio);

    private static string FormatTime(TimeSpan ts)
    {
        if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}

public sealed class MediaModule : IIslandModule
{
    private ModuleContext? _ctx;
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private MediaActivity? _activity;
    private DispatcherTimer? _timelineTimer;
    private DateTime _lastTimelineAnchorUtc;
    private TimeSpan _anchorPosition;
    private string _lastArtKey = string.Empty;
    private int _refreshQueued;
    private bool _disposed;

    public string ModuleId => "media";
    public string DisplayName => "Media Player";
    public bool IsEnabled { get; private set; }
    public bool IsActive => _activity is not null && IsEnabled;

    public async Task InitializeAsync(ModuleContext context)
    {
        _ctx = context;
        IsEnabled = context.SettingsService.Current.EnableMedia;

        _ctx.AudioService.SpectrumUpdated += OnSpectrumUpdated;
        _ctx.SettingsService.SettingsChanged += OnSettingsChanged;

        if (IsEnabled)
        {
            await AttachSessionManagerAsync();
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled || _ctx is null) return;
        IsEnabled = enabled;

        if (enabled)
        {
            _ = AttachSessionManagerAsync();
        }
        else
        {
            DetachSessionManager();
            _ctx.AudioService.SetVisualizerActive(false);
            StopTimelineTimer();
            _ctx.ActivityManager.Remove("media.current");
            _activity = null;
        }
    }

    private void OnSettingsChanged(object? sender, Settings.AppSettings settings)
    {
        if (settings.EnableMedia != IsEnabled)
        {
            SetEnabled(settings.EnableMedia);
        }
        if (_activity is not null && settings.LowPowerMode)
        {
            _ctx?.AudioService.SetVisualizerActive(false);
        }
        else if (_activity is not null && _activity.IsPlaying && !settings.LowPowerMode)
        {
            _ctx?.AudioService.SetVisualizerActive(true);
        }
    }

    private async Task AttachSessionManagerAsync()
    {
        try
        {
            _sessionManager ??= await GlobalSystemMediaTransportControlsSessionManager
                .RequestAsync()
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(3));
            _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
            _sessionManager.SessionsChanged += OnSessionsChanged;
            await RefreshSessionAsync();
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Warn("Media", "GSMTC unavailable on this OS/session.", ex);
        }
    }

    private void DetachSessionManager()
    {
        DetachCurrentSession();
        if (_sessionManager is not null)
        {
            _sessionManager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _sessionManager.SessionsChanged -= OnSessionsChanged;
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        ScheduleSessionRefresh();
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        ScheduleSessionRefresh();
    }

    private void ScheduleSessionRefresh()
    {
        if (_disposed || _ctx is null) return;
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 0)
        {
            _ctx.UiDispatcher.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref _refreshQueued, 0);
                _ = RefreshSessionAsync();
            });
        }
    }

    private async Task RefreshSessionAsync()
    {
        if (!IsEnabled || _disposed || _sessionManager is null || _ctx is null) return;

        var next = _sessionManager.GetCurrentSession() ?? _sessionManager.GetSessions().FirstOrDefault();
        if (!ReferenceEquals(next, _currentSession))
        {
            DetachCurrentSession();
            _currentSession = next;
            if (_currentSession is not null)
            {
                _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
            }
        }

        if (_currentSession is null)
        {
            _ctx.AudioService.SetVisualizerActive(false);
            StopTimelineTimer();
            _ctx.ActivityManager.Remove("media.current");
            _activity = null;
            return;
        }

        await PullAllSessionDataAsync(_currentSession);
    }

    private void DetachCurrentSession()
    {
        if (_currentSession is null) return;
        _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        _currentSession = null;
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        _ctx?.UiDispatcher.BeginInvoke(() => _ = PullAllSessionDataAsync(sender));
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        _ctx?.UiDispatcher.BeginInvoke(() => UpdatePlaybackInfo(sender));
    }

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        _ctx?.UiDispatcher.BeginInvoke(() => UpdateTimeline(sender));
    }

    private async Task PullAllSessionDataAsync(GlobalSystemMediaTransportControlsSession session)
    {
        if (!IsEnabled || _disposed || _ctx is null) return;

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (props is null) return;

            string title = props.Title ?? string.Empty;
            string artist = props.Artist ?? string.Empty;
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist))
            {
                return;
            }

            ImageSource? art = null;
            Color dominant = Color.FromRgb(10, 132, 255);
            string artKey = $"{session.SourceAppUserModelId}|{title}|{artist}";

            if (string.Equals(artKey, _lastArtKey, StringComparison.Ordinal) && _activity?.Thumbnail is not null)
            {
                art = _activity.Thumbnail;
                dominant = _activity.AccentColor;
            }
            else if (props.Thumbnail is not null)
            {
                byte[]? bytes = await ReadStreamBytesAsync(props.Thumbnail);
                if (bytes is { Length: > 0 })
                {
                    var cached = _ctx.ImageCache.GetOrDecode(bytes, artKey);
                    if (cached is not null)
                    {
                        art = cached.Value.Image;
                        dominant = cached.Value.DominantColor;
                    }
                }
            }

            _lastArtKey = artKey;

            _activity ??= new MediaActivity(this);
            _activity.UpdateMetadata(title, artist, FormatAppName(session.SourceAppUserModelId), art, dominant);

            UpdatePlaybackInfo(session);
            UpdateTimeline(session);
            _ctx.ActivityManager.RegisterOrUpdate(_activity);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Debug("Media", $"Failed reading GSMTC metadata: {ex.Message}");
        }
    }

    private void UpdatePlaybackInfo(GlobalSystemMediaTransportControlsSession session)
    {
        if (_activity is null || _ctx is null) return;
        try
        {
            var info = session.GetPlaybackInfo();
            bool playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            _activity.IsPlaying = playing;

            bool useVisualizer = playing && IsEnabled && !_ctx.SettingsService.Current.LowPowerMode;
            _ctx.AudioService.SetVisualizerActive(useVisualizer);

            if (playing) StartTimelineTimer();
            else StopTimelineTimer();
        }
        catch
        {
            // Session disposed mid-read
        }
    }

    private void UpdateTimeline(GlobalSystemMediaTransportControlsSession session)
    {
        if (_activity is null) return;
        try
        {
            var tl = session.GetTimelineProperties();
            _anchorPosition = tl.Position;
            _lastTimelineAnchorUtc = tl.LastUpdatedTime.UtcDateTime == DateTime.MinValue
                ? DateTime.UtcNow
                : tl.LastUpdatedTime.UtcDateTime;

            _activity.Duration = tl.EndTime > tl.StartTime ? tl.EndTime - tl.StartTime : TimeSpan.Zero;
            _activity.Position = ComputeInterpolatedPosition();
        }
        catch
        {
            // Ignore
        }
    }

    private TimeSpan ComputeInterpolatedPosition()
    {
        if (_activity is null) return TimeSpan.Zero;
        if (!_activity.IsPlaying) return _anchorPosition;

        var elapsed = DateTime.UtcNow - _lastTimelineAnchorUtc;
        if (elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromHours(12)) elapsed = TimeSpan.Zero;
        var pos = _anchorPosition + elapsed;
        if (_activity.Duration > TimeSpan.Zero && pos > _activity.Duration) pos = _activity.Duration;
        return pos;
    }

    private void StartTimelineTimer()
    {
        if (_ctx is null || _timelineTimer is not null) return;
        _timelineTimer = new DispatcherTimer(DispatcherPriority.Background, _ctx.UiDispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timelineTimer.Tick += (_, _) =>
        {
            if (_activity is not null && _activity.IsPlaying)
            {
                _activity.Position = ComputeInterpolatedPosition();
            }
        };
        _timelineTimer.Start();
    }

    private void StopTimelineTimer()
    {
        if (_timelineTimer is null) return;
        _timelineTimer.Stop();
        _timelineTimer = null;
    }

    private void OnSpectrumUpdated(object? sender, float[] bands)
    {
        if (!IsEnabled || _activity is null || !_activity.IsPlaying) return;
        _ctx?.UiDispatcher.BeginInvoke(() =>
        {
            if (_activity is not null)
            {
                _activity.Spectrum = bands;
            }
        });
    }

    public async Task TogglePlayPauseAsync()
    {
        if (_currentSession is null) return;
        try { await _currentSession.TryTogglePlayPauseAsync(); } catch { }
    }

    public async Task NextTrackAsync()
    {
        if (_currentSession is null) return;
        try { await _currentSession.TrySkipNextAsync(); } catch { }
    }

    public async Task PreviousTrackAsync()
    {
        if (_currentSession is null) return;
        try { await _currentSession.TrySkipPreviousAsync(); } catch { }
    }

    public async Task SeekRatioAsync(double ratio)
    {
        if (_currentSession is null || _activity is null || _activity.Duration <= TimeSpan.Zero) return;
        try
        {
            long targetTicks = (long)(_activity.Duration.Ticks * Math.Clamp(ratio, 0.0, 1.0));
            await _currentSession.TryChangePlaybackPositionAsync(targetTicks);
            _anchorPosition = TimeSpan.FromTicks(targetTicks);
            _lastTimelineAnchorUtc = DateTime.UtcNow;
            _activity.Position = _anchorPosition;
        }
        catch
        {
            // Some players don't support seeking
        }
    }

    private static async Task<byte[]?> ReadStreamBytesAsync(IRandomAccessStreamReference streamRef)
    {
        try
        {
            using var stream = await streamRef.OpenReadAsync();
            using var netStream = stream.AsStreamForRead();
            using var ms = new MemoryStream();
            await netStream.CopyToAsync(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static string FormatAppName(string aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return "Media";
        if (aumid.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (aumid.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase)) return "Apple Music";
        if (aumid.Contains("vlc", StringComparison.OrdinalIgnoreCase)) return "VLC";
        if (aumid.Contains("chrome", StringComparison.OrdinalIgnoreCase)) return "Chrome";
        if (aumid.Contains("msedge", StringComparison.OrdinalIgnoreCase)) return "Edge";
        if (aumid.Contains("firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        int bang = aumid.IndexOf('!');
        string clean = bang > 0 ? aumid[..bang] : aumid;
        return clean.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DetachSessionManager();
        StopTimelineTimer();
        if (_ctx is not null)
        {
            _ctx.AudioService.SpectrumUpdated -= OnSpectrumUpdated;
            _ctx.SettingsService.SettingsChanged -= OnSettingsChanged;
            _ctx.AudioService.SetVisualizerActive(false);
        }
    }
}

