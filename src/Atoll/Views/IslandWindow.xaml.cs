using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Atoll.Core.Activities;
using Atoll.Core.Animation;
using Atoll.Core.State;
using Atoll.Core.Window;
using Atoll.Modules.Calls;
using Atoll.Modules.Glance;
using Atoll.Modules.Media;
using Atoll.Modules.Notifications;
using Atoll.Modules.Osd;
using Atoll.Modules.Privacy;
using Atoll.Modules.QuickActions;
using Atoll.Modules.Shelf;
using Atoll.Modules.Timer;
using Atoll.Services;
using Atoll.Settings;

namespace Atoll.Views;

public partial class IslandWindow : System.Windows.Window
{
    public const double OverlayWidthDip = 580.0;
    public const double OverlayHeightDip = 310.0;

    private readonly SettingsService _settingsService;
    private readonly DisplayMonitorService _monitorService;
    private readonly ActivityManager _activityManager;
    private readonly IslandStateMachine _stateMachine;
    private readonly MorphAnimator _animator;
    private readonly AudioEndpointService _audioService;
    private readonly IslandWindowController _windowController;
    private readonly FullscreenDetector _fullscreenDetector;

    private readonly MediaModule _mediaModule;
    private readonly TimerModule _timerModule;
    private readonly CallModule _callModule;
    private readonly PrivacyModule _privacyModule;
    private readonly FileShelfModule _shelfModule;
    private readonly QuickActionsModule _quickActionsModule;
    private readonly GlanceModule _glanceModule;
    private readonly OsdModule _osdModule;

    private readonly Action _openSettingsAction;
    private readonly DispatcherTimer _longPressTimer;
    private readonly DispatcherTimer _hoverExpandTimer;
    private readonly SquircleGeometryCache _mainPillGeoCache = new();
    private readonly SquircleGeometryCache _outerShadowGeoCache = new();
    private readonly SquircleGeometryCache _innerShadowGeoCache = new();
    private readonly SquircleGeometryCache _bubbleGeoCache = new();
    private readonly SquircleGeometryCache _bubbleShadowGeoCache = new();
    private readonly Dictionary<Color, SolidColorBrush> _frozenBrushes = new();

    private Point _pointerDownPos;
    private bool _isPointerDown;
    private bool _longPressTriggered;
    private bool _isSeekingMedia;
    private bool _forceQuickActionsTab;
    private bool _bubbleCollapsed = true;
    private IIslandActivity? _observedActivity;
    private IslandState _lastVisualState = IslandState.Idle;
    private string? _lastVisualActivityId;
    private Color _lastGlowColor;
    private Color _lastCompactGlyphBgColor;
    private Color _lastAlertIconBgColor;
    private Color _lastAlertSliderColor;

    private Rect _hitPillRect = new(230, 8, 120, 34);
    private double _hitPillRadius = 17;
    private Rect _hitBubbleRect = Rect.Empty;
    private double _hitBubbleRadius;

    public IslandWindow(
        SettingsService settingsService,
        DisplayMonitorService monitorService,
        ActivityManager activityManager,
        IslandStateMachine stateMachine,
        MorphAnimator animator,
        AudioEndpointService audioService,
        FullscreenDetector fullscreenDetector,
        MediaModule mediaModule,
        TimerModule timerModule,
        CallModule callModule,
        PrivacyModule privacyModule,
        FileShelfModule shelfModule,
        QuickActionsModule quickActionsModule,
        GlanceModule glanceModule,
        OsdModule osdModule,
        Action openSettingsAction)
    {
        InitializeComponent();

        _settingsService = settingsService;
        _monitorService = monitorService;
        _activityManager = activityManager;
        _stateMachine = stateMachine;
        _animator = animator;
        _audioService = audioService;
        _fullscreenDetector = fullscreenDetector;
        _mediaModule = mediaModule;
        _timerModule = timerModule;
        _callModule = callModule;
        _privacyModule = privacyModule;
        _shelfModule = shelfModule;
        _quickActionsModule = quickActionsModule;
        _glanceModule = glanceModule;
        _osdModule = osdModule;
        _openSettingsAction = openSettingsAction;

        _windowController = new IslandWindowController(this, _monitorService, IsDipPointInsidePill);
        _windowController.DisplayOrDpiChanged += OnDisplayOrDpiChanged;

        _hoverExpandTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
        _hoverExpandTimer.Tick += OnHoverExpandTimerTick;

        _longPressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
        _longPressTimer.Tick += OnLongPressTimerTick;

        _animator.SpeedMultiplier = _settingsService.Current.AnimationSpeed;
        _animator.LowPowerMode = _settingsService.Current.LowPowerMode;
        _animator.FrameUpdated += OnAnimatorFrameUpdated;
        _animator.Settled += OnAnimatorSettled;

        _stateMachine.StateChanged += OnIslandStateChanged;
        _activityManager.ActivitiesChanged += OnActivitiesChanged;
        _privacyModule.PrivacyDotsChanged += OnPrivacyDotsChanged;
        _quickActionsModule.StateUpdated += OnQuickActionsOrGlanceUpdated;
        _glanceModule.GlanceUpdated += OnQuickActionsOrGlanceUpdated;
        _settingsService.SettingsChanged += OnSettingsChanged;
        _fullscreenDetector.FullscreenChanged += OnFullscreenChanged;

        MouseEnter += OnPillMouseEnter;
        MouseLeave += OnPillMouseLeave;
        PreviewMouseLeftButtonDown += OnPillMouseDown;
        PreviewMouseMove += OnPillMouseMove;
        PreviewMouseLeftButtonUp += OnPillMouseUp;
        PreviewMouseRightButtonUp += OnPillRightClick;
        PreviewMouseWheel += OnPillMouseWheel;
        DragEnter += OnPillDragEnter;
        Drop += OnPillDrop;
    }

    public IslandWindowController WindowController => _windowController;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowController.Attach();
        RepositionWindow();
        _fullscreenDetector.Start(_windowController.Handle);

        ApplyStateLayout(_stateMachine.CurrentState, snapImmediately: true);
    }

    public void RepositionWindow()
    {
        var s = _settingsService.Current;
        _windowController.PositionOnMonitor(s.MonitorDeviceName, OverlayWidthDip, OverlayHeightDip, s.HorizontalOffset);
        ApplyStateLayout(_stateMachine.CurrentState, snapImmediately: false);
    }

    private bool IsDipPointInsidePill(Point dipPoint)
    {
        if (IslandGeometry.HitTestRoundedRect(dipPoint, _hitPillRect, _hitPillRadius))
        {
            return true;
        }
        if (_hitBubbleRect != Rect.Empty &&
            IslandGeometry.HitTestRoundedRect(dipPoint, _hitBubbleRect, _hitBubbleRadius))
        {
            return true;
        }
        return false;
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _animator.SpeedMultiplier = settings.AnimationSpeed;
            _animator.LowPowerMode = settings.LowPowerMode;
            RepositionWindow();
            if (settings.LowPowerMode)
            {
                AmbientGlow.Opacity = 0;
            }
        });
    }

    private void OnFullscreenChanged(object? sender, bool isFullscreen)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!_settingsService.Current.AutoHideFullscreen)
            {
                _stateMachine.SetFullscreenSuppressed(false);
                ApplyStateLayout(_stateMachine.CurrentState, snapImmediately: false);
                return;
            }

            _stateMachine.SetFullscreenSuppressed(isFullscreen);
            ApplyStateLayout(_stateMachine.CurrentState, snapImmediately: false);
        });
    }

    private void OnActivitiesChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            RebindObservedActivity();
            if (_stateMachine.CurrentState != IslandState.Alert)
            {
                ApplyStateLayout(_stateMachine.CurrentState, snapImmediately: false);
            }
        });
    }

    private void OnIslandStateChanged(object? sender, IslandStateChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (e.NewState != IslandState.Expanded)
            {
                _forceQuickActionsTab = false;
            }
            else
            {
                _ = _quickActionsModule.RefreshOnExpandAsync();
                if (_activityManager.PrimaryActivity is null)
                {
                    _ = _glanceModule.RefreshOnIdleExpandAsync();
                }
            }

            RebindObservedActivity();
            ApplyStateLayout(e.NewState, snapImmediately: false);
        });
    }

    private void RebindObservedActivity()
    {
        var next = _stateMachine.CurrentState == IslandState.Alert
            ? _stateMachine.ActiveAlertActivity
            : _activityManager.PrimaryActivity;

        if (!ReferenceEquals(_observedActivity, next))
        {
            if (_observedActivity is not null)
            {
                _observedActivity.PropertyChanged -= OnObservedActivityPropertyChanged;
            }
            _observedActivity = next;
            if (_observedActivity is not null)
            {
                _observedActivity.PropertyChanged += OnObservedActivityPropertyChanged;
            }
        }
    }

    private void OnDisplayOrDpiChanged(object? sender, EventArgs e) => RepositionWindow();
    private void OnPrivacyDotsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(UpdatePrivacyDots);
    private void OnQuickActionsOrGlanceUpdated(object? sender, EventArgs e) => Dispatcher.BeginInvoke(BindQuickActionsPanel);

    private void OnObservedActivityPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaActivity.Spectrum))
        {
            Dispatcher.BeginInvoke(UpdateMediaSpectrumOnly);
            return;
        }

        if (e.PropertyName is nameof(MediaActivity.Position) or nameof(MediaActivity.PositionText) or nameof(MediaActivity.ProgressRatio))
        {
            Dispatcher.BeginInvoke(UpdateMediaProgressOnly);
            return;
        }

        Dispatcher.BeginInvoke(RefreshActiveViewData);
    }

    private void UpdateMediaSpectrumOnly()
    {
        if (_observedActivity is not MediaActivity media) return;
        var state = _stateMachine.CurrentState;
        if (state is IslandState.Compact or IslandState.Minimal)
        {
            CompactVisualizer.Bands = media.Spectrum;
            CompactVisualizer.IsPlaying = media.IsPlaying;
        }
        else if (state == IslandState.Expanded && !_forceQuickActionsTab)
        {
            MediaExpandedVisualizer.Bands = media.Spectrum;
            MediaExpandedVisualizer.IsPlaying = media.IsPlaying;
        }
    }

    private void UpdateMediaProgressOnly()
    {
        if (_observedActivity is not MediaActivity media) return;
        if (_stateMachine.CurrentState != IslandState.Expanded || _forceQuickActionsTab) return;
        MediaPosText.Text = media.PositionText;
        MediaDurText.Text = media.DurationText;
        if (!_isSeekingMedia)
        {
            MediaSeekSlider.Value = media.ProgressRatio;
        }
    }

    private void ApplyStateLayout(IslandState state, bool snapImmediately)
    {
        var s = _settingsService.Current;
        double scale = s.SizeScale;
        double baseTop = s.NotchMode ? 0.0 : s.TopOffset;

        bool hiddenForFullscreen = _stateMachine.IsFullscreenSuppressed && s.AutoHideFullscreen && !s.ShrinkInsteadOfHideInFullscreen;
        bool shrinkForFullscreen = _stateMachine.IsFullscreenSuppressed && s.AutoHideFullscreen && s.ShrinkInsteadOfHideInFullscreen;
        double pillOpacity = (_stateMachine.IsPaused || hiddenForFullscreen) ? 0.0 : 1.0;

        double targetW, targetH, targetR;
        double bubbleSize = 0.0, bubbleGap = 0.0, bubbleOpacity = 0.0;

        if (shrinkForFullscreen && state != IslandState.Expanded)
        {
            targetW = 86 * scale;
            targetH = 22 * scale;
            targetR = 11 * scale;
        }
        else
        {
            switch (state)
            {
                case IslandState.Idle:
                    targetW = (_privacyModule.CameraActive || _privacyModule.MicActive || _privacyModule.ScreenCaptureActive ? 142 : 120) * scale;
                    targetH = 34 * scale;
                    targetR = targetH * 0.5;
                    break;

                case IslandState.Compact:
                {
                    var p = _activityManager.PrimaryActivity;
                    Size sz = p?.PreferredCompactSize ?? new Size(216, 36);
                    targetW = sz.Width * scale;
                    targetH = sz.Height * scale;
                    targetR = targetH * 0.5;
                    break;
                }

                case IslandState.Minimal:
                {
                    var p = _activityManager.PrimaryActivity;
                    Size sz = p?.PreferredCompactSize ?? new Size(210, 36);
                    targetW = sz.Width * scale;
                    targetH = sz.Height * scale;
                    targetR = targetH * 0.5;
                    bubbleSize = targetH;
                    bubbleGap = 8 * scale;
                    bubbleOpacity = _activityManager.SecondaryActivity is not null ? 1.0 : 0.0;
                    break;
                }

                case IslandState.Alert:
                {
                    var a = _stateMachine.ActiveAlertActivity;
                    Size sz = a?.PreferredAlertSize ?? new Size(336, 78);
                    targetW = sz.Width * scale;
                    targetH = sz.Height * scale;
                    targetR = 24 * scale;
                    break;
                }

                case IslandState.Expanded:
                {
                    var p = _activityManager.PrimaryActivity;
                    Size sz = (_forceQuickActionsTab || p is null)
                        ? new Size(388, _glanceModule.IsEnabled ? 210 : 172)
                        : p.PreferredExpandedSize;
                    targetW = sz.Width * scale;
                    targetH = sz.Height * scale;
                    targetR = 28 * scale;
                    break;
                }

                default:
                    targetW = 120;
                    targetH = 34;
                    targetR = 17;
                    break;
            }
        }

        string? currentVisualId = state == IslandState.Alert
            ? _stateMachine.ActiveAlertActivity?.Id
            : _activityManager.PrimaryActivity?.Id;
        bool visualChanged = state != _lastVisualState ||
                             !string.Equals(currentVisualId, _lastVisualActivityId, StringComparison.Ordinal);
        _lastVisualState = state;
        _lastVisualActivityId = currentVisualId;

        UpdateActivePanelVisibility(state);
        RefreshActiveViewData();

        if (snapImmediately)
        {
            _animator.SnapAll(targetW, targetH, targetR, baseTop, bubbleSize, bubbleGap, bubbleOpacity, pillOpacity);
        }
        else
        {
            _animator.SetLayoutTargets(targetW, targetH, targetR, baseTop, bubbleSize, bubbleGap, bubbleOpacity, pillOpacity, triggerContentStagger: visualChanged);
        }
    }

    private void OnAnimatorFrameUpdated(MorphFrame frame)
    {
        double w = frame.Width * frame.PressScale;
        double h = frame.Height * frame.PressScale;
        double r = frame.CornerRadius * frame.PressScale;

        double totalSpan = w + (frame.BubbleOpacity > 0.05 ? frame.BubbleGap + frame.BubbleSize : 0.0);
        double pillX = (OverlayWidthDip - totalSpan) * 0.5;
        double pillY = frame.TopOffset;

        _hitPillRect = new Rect(pillX, pillY, w, h);
        _hitPillRadius = r;

        MainPillPath.Opacity = frame.PillOpacity;
        PillShadowOuter.Opacity = frame.PillOpacity;
        PillShadowInner.Opacity = frame.PillOpacity;

        MainPillPath.Data = _mainPillGeoCache.GetOrCreate(pillX, pillY, w, h, r);
        PillShadowOuter.Data = _outerShadowGeoCache.GetOrCreate(pillX - 4, pillY + 2, w + 8, h + 8, r + 4);
        PillShadowInner.Data = _innerShadowGeoCache.GetOrCreate(pillX - 2, pillY + 1, w + 4, h + 4, r + 2);

        Canvas.SetLeft(MainContentHost, pillX);
        Canvas.SetTop(MainContentHost, pillY);
        MainContentHost.Width = w;
        MainContentHost.Height = h;
        MainContentHost.Opacity = frame.ContentOpacity * frame.PillOpacity;
        ContentScaleTransform.ScaleX = frame.ContentScale;
        ContentScaleTransform.ScaleY = frame.ContentScale;

        // Ambient glow behind pill
        if (!_settingsService.Current.LowPowerMode && frame.PillOpacity > 0.1 && _stateMachine.CurrentState != IslandState.Idle)
        {
            Canvas.SetLeft(AmbientGlow, pillX + (w - AmbientGlow.Width) * 0.5);
            Canvas.SetTop(AmbientGlow, pillY - 8);
            AmbientGlow.Opacity = _stateMachine.CurrentState == IslandState.Expanded ? 0.55 : 0.30;
        }
        else
        {
            AmbientGlow.Opacity = 0;
        }

        // Minimal detached split bubble
        if (frame.BubbleOpacity > 0.04 && frame.BubbleSize > 4)
        {
            double bSize = frame.BubbleSize * frame.PressScale;
            double bX = pillX + w + frame.BubbleGap;
            double bY = pillY + (h - bSize) * 0.5;

            _hitBubbleRect = new Rect(bX, bY, bSize, bSize);
            _hitBubbleRadius = bSize * 0.5;

            if (_bubbleCollapsed)
            {
                _bubbleCollapsed = false;
                DetachedBubblePath.Visibility = Visibility.Visible;
                BubbleShadow.Visibility = Visibility.Visible;
                BubbleContentHost.Visibility = Visibility.Visible;
            }

            DetachedBubblePath.Opacity = frame.BubbleOpacity * frame.PillOpacity;
            BubbleShadow.Opacity = frame.BubbleOpacity * frame.PillOpacity;
            BubbleContentHost.Opacity = frame.BubbleOpacity * frame.PillOpacity;

            DetachedBubblePath.Data = _bubbleGeoCache.GetOrCreate(bX, bY, bSize, bSize, bSize * 0.5);
            BubbleShadow.Data = _bubbleShadowGeoCache.GetOrCreate(bX - 2, bY + 1, bSize + 4, bSize + 4, (bSize + 4) * 0.5);

            Canvas.SetLeft(BubbleContentHost, bX);
            Canvas.SetTop(BubbleContentHost, bY);
            BubbleContentHost.Width = bSize;
            BubbleContentHost.Height = bSize;
        }
        else if (!_bubbleCollapsed)
        {
            _bubbleCollapsed = true;
            _hitBubbleRect = Rect.Empty;
            DetachedBubblePath.Visibility = Visibility.Collapsed;
            BubbleShadow.Visibility = Visibility.Collapsed;
            BubbleContentHost.Visibility = Visibility.Collapsed;
        }
    }

    private void OnAnimatorSettled()
    {
        if (_stateMachine.CurrentState == IslandState.Idle)
        {
            AmbientGlow.Opacity = 0;
        }
    }

    private void UpdateActivePanelVisibility(IslandState state)
    {
        IdlePanel.Visibility = state == IslandState.Idle ? Visibility.Visible : Visibility.Collapsed;
        CompactPanel.Visibility = state is IslandState.Compact or IslandState.Minimal ? Visibility.Visible : Visibility.Collapsed;
        AlertPanel.Visibility = state == IslandState.Alert ? Visibility.Visible : Visibility.Collapsed;
        ExpandedPanel.Visibility = state == IslandState.Expanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshActiveViewData()
    {
        UpdatePrivacyDots();

        var state = _stateMachine.CurrentState;
        if (state == IslandState.Alert && _stateMachine.ActiveAlertActivity is { } alert)
        {
            BindAlertPanel(alert);
            UpdateGlowColor(alert.AccentColor);
            return;
        }

        var primary = _activityManager.PrimaryActivity;
        if (primary is not null)
        {
            UpdateGlowColor(primary.AccentColor);
        }
        else
        {
            UpdateGlowColor(ParseAccent(_settingsService.Current.AccentColorHex));
        }

        if (state is IslandState.Compact or IslandState.Minimal && primary is not null)
        {
            BindCompactPanel(primary);
            if (state == IslandState.Minimal && _activityManager.SecondaryActivity is { } secondary)
            {
                BindMinimalBubble(secondary);
            }
        }
        else if (state == IslandState.Expanded)
        {
            BindExpandedPanel(primary);
        }
    }

    private void UpdatePrivacyDots()
    {
        var camVis = _privacyModule.CameraActive ? Visibility.Visible : Visibility.Collapsed;
        var micVis = _privacyModule.MicActive ? Visibility.Visible : Visibility.Collapsed;
        var recVis = _privacyModule.ScreenCaptureActive ? Visibility.Visible : Visibility.Collapsed;

        IdleCamDot.Visibility = camVis;
        IdleMicDot.Visibility = micVis;
        IdleRecDot.Visibility = recVis;

        CompactCamDot.Visibility = camVis;
        CompactMicDot.Visibility = micVis;
    }

    private void UpdateGlowColor(Color c)
    {
        if (_lastGlowColor == c) return;
        _lastGlowColor = c;
        GlowStopInner.Color = Color.FromArgb(95, c.R, c.G, c.B);
    }

    private SolidColorBrush GetFrozenBrush(Color color)
    {
        if (_frozenBrushes.TryGetValue(color, out var existing))
        {
            return existing;
        }

        if (_frozenBrushes.Count >= 32)
        {
            _frozenBrushes.Clear();
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        _frozenBrushes[color] = brush;
        return brush;
    }

    private void BindCompactPanel(IIslandActivity activity)
    {
        if (activity.Thumbnail is not null)
        {
            CompactThumbBorder.Visibility = Visibility.Visible;
            CompactThumbImage.Source = activity.Thumbnail;
            CompactGlyphBg.Visibility = Visibility.Collapsed;
        }
        else
        {
            CompactThumbBorder.Visibility = Visibility.Collapsed;
            CompactGlyphBg.Visibility = Visibility.Visible;
            CompactGlyphText.Text = activity.Glyph;
            Color bgColor = Color.FromArgb(55, activity.AccentColor.R, activity.AccentColor.G, activity.AccentColor.B);
            if (_lastCompactGlyphBgColor != bgColor)
            {
                _lastCompactGlyphBgColor = bgColor;
                CompactGlyphBg.Background = GetFrozenBrush(bgColor);
            }
        }

        CompactTitleText.Text = activity.Title;

        if (activity is MediaActivity media)
        {
            CompactVisualizer.Visibility = Visibility.Visible;
            CompactVisualizer.Bands = media.Spectrum;
            CompactVisualizer.IsPlaying = media.IsPlaying;
            CompactVisualizer.BarColor = media.AccentColor;
            CompactRing.Visibility = Visibility.Collapsed;
            CompactTrailingLabel.Text = string.Empty;
        }
        else if (activity.Progress.HasValue)
        {
            CompactVisualizer.Visibility = Visibility.Collapsed;
            CompactRing.Visibility = Visibility.Visible;
            CompactRing.Progress = activity.Progress.Value;
            CompactRing.RingColor = activity.AccentColor;
            CompactTrailingLabel.Text = activity.CompactTrailingText;
        }
        else
        {
            CompactVisualizer.Visibility = Visibility.Collapsed;
            CompactRing.Visibility = Visibility.Collapsed;
            CompactTrailingLabel.Text = activity.CompactTrailingText;
        }
    }

    private void BindMinimalBubble(IIslandActivity secondary)
    {
        if (secondary.Thumbnail is not null)
        {
            BubbleThumbBorder.Visibility = Visibility.Visible;
            BubbleThumbImage.Source = secondary.Thumbnail;
            BubbleGlyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            BubbleThumbBorder.Visibility = Visibility.Collapsed;
            BubbleGlyph.Visibility = Visibility.Visible;
            BubbleGlyph.Text = secondary.Glyph;
        }

        if (secondary.Progress.HasValue)
        {
            BubbleRing.Visibility = Visibility.Visible;
            BubbleRing.Progress = secondary.Progress.Value;
            BubbleRing.RingColor = secondary.AccentColor;
        }
        else
        {
            BubbleRing.Visibility = Visibility.Collapsed;
        }
    }

    private void BindAlertPanel(IIslandActivity alert)
    {
        if (alert.Thumbnail is not null)
        {
            AlertThumbBorder.Visibility = Visibility.Visible;
            AlertThumbImage.Source = alert.Thumbnail;
            AlertIconBg.Visibility = Visibility.Collapsed;
        }
        else
        {
            AlertThumbBorder.Visibility = Visibility.Collapsed;
            AlertIconBg.Visibility = Visibility.Visible;
            AlertGlyphText.Text = alert.Glyph;
            Color bgColor = Color.FromArgb(55, alert.AccentColor.R, alert.AccentColor.G, alert.AccentColor.B);
            if (_lastAlertIconBgColor != bgColor)
            {
                _lastAlertIconBgColor = bgColor;
                AlertIconBg.Background = GetFrozenBrush(bgColor);
            }
        }

        AlertTitleText.Text = alert.Title;
        AlertSubtitleText.Text = alert.Subtitle;

        if (alert is OsdActivity osd)
        {
            AlertOsdSlider.Visibility = Visibility.Visible;
            AlertOsdSlider.Value = osd.ValuePercent;
            if (_lastAlertSliderColor != osd.AccentColor)
            {
                _lastAlertSliderColor = osd.AccentColor;
                AlertOsdSlider.Foreground = GetFrozenBrush(osd.AccentColor);
            }
            AlertTrailingText.Text = osd.ValueText;
            AlertRing.Visibility = Visibility.Collapsed;
        }
        else
        {
            AlertOsdSlider.Visibility = Visibility.Collapsed;
            AlertTrailingText.Text = alert.CompactTrailingText;
            if (alert.Progress.HasValue)
            {
                AlertRing.Visibility = Visibility.Visible;
                AlertRing.Progress = alert.Progress.Value;
                AlertRing.RingColor = alert.AccentColor;
            }
            else
            {
                AlertRing.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void BindExpandedPanel(IIslandActivity? primary)
    {
        bool hasMultiple = _activityManager.Activities.Count > 1;
        PrevActivityBtn.Visibility = hasMultiple ? Visibility.Visible : Visibility.Collapsed;
        NextActivityBtn.Visibility = hasMultiple ? Visibility.Visible : Visibility.Collapsed;

        MediaCard.Visibility = Visibility.Collapsed;
        TimerCard.Visibility = Visibility.Collapsed;
        CallCard.Visibility = Visibility.Collapsed;
        ShelfCard.Visibility = Visibility.Collapsed;
        GenericActivityCard.Visibility = Visibility.Collapsed;
        QuickActionsCard.Visibility = Visibility.Collapsed;

        if (_forceQuickActionsTab || primary is null)
        {
            ExpandedHeaderLabel.Text = "QUICK ACTIONS";
            QuickActionsCard.Visibility = Visibility.Visible;
            BindQuickActionsPanel();
            return;
        }

        ExpandedHeaderLabel.Text = primary.ModuleId.ToUpperInvariant();

        switch (primary)
        {
            case MediaActivity media:
                MediaCard.Visibility = Visibility.Visible;
                MediaArtImage.Source = media.Thumbnail;
                MediaTitleText.Text = media.Title;
                MediaArtistText.Text = media.Subtitle;
                MediaPlayPauseBtn.Content = media.PlayPauseGlyph;
                MediaPosText.Text = media.PositionText;
                MediaDurText.Text = media.DurationText;
                MediaExpandedVisualizer.Bands = media.Spectrum;
                MediaExpandedVisualizer.IsPlaying = media.IsPlaying;
                MediaExpandedVisualizer.BarColor = media.AccentColor;
                if (!_isSeekingMedia)
                {
                    MediaSeekSlider.Value = media.ProgressRatio;
                }
                break;

            case TimerActivity timer:
                TimerCard.Visibility = Visibility.Visible;
                TimerExpandedRing.Progress = timer.Progress ?? 0.0;
                TimerExpandedLabel.Text = timer.Title;
                TimerExpandedReadout.Text = timer.FormattedTime;
                TimerPlayPauseBtn.Content = timer.PlayPauseGlyph;
                break;

            case CallActivity call:
                CallCard.Visibility = Visibility.Visible;
                CallAppText.Text = call.Title;
                CallDurationText.Text = call.DurationText;
                CallMuteLabel.Text = call.MuteLabel;
                break;

            case FileShelfActivity shelf:
                ShelfCard.Visibility = Visibility.Visible;
                ShelfListBox.ItemsSource = shelf.Items;
                break;

            default:
                GenericActivityCard.Visibility = Visibility.Visible;
                GenericTitle.Text = primary.Title;
                GenericSubtitle.Text = primary.Subtitle;
                GenericRing.Progress = primary.Progress ?? 1.0;
                GenericRing.RingColor = primary.AccentColor;
                break;
        }
    }

    private void BindQuickActionsPanel()
    {
        DndToggleLabel.Text = _quickActionsModule.IsDndEnabled ? "Focus: On" : "Focus: Off";
        ClipboardItemsList.ItemsSource = _quickActionsModule.RecentClipboardItems;

        if (_glanceModule.IsEnabled)
        {
            GlanceRow.Visibility = Visibility.Visible;
            GlanceTimeText.Text = _glanceModule.CurrentTimeText;
            GlanceDateText.Text = _glanceModule.CurrentDateText;
            GlanceWeatherText.Text = _glanceModule.WeatherSummary;
        }
        else
        {
            GlanceRow.Visibility = Visibility.Collapsed;
        }
    }

    // --- Gesture Handlers ---

    private void OnHoverExpandTimerTick(object? sender, EventArgs e)
    {
        _hoverExpandTimer.Stop();
        if (IsMouseOver) _stateMachine.Expand();
    }

    private void OnLongPressTimerTick(object? sender, EventArgs e)
    {
        _longPressTimer.Stop();
        if (_isPointerDown)
        {
            _longPressTriggered = true;
            _animator.SetScaleTarget(1.02, impulseVelocity: 0.4);
            _stateMachine.Expand();
        }
    }

    private void OnPillMouseEnter(object sender, MouseEventArgs e)
    {
        _animator.SetScaleTarget(1.035);
        if (_settingsService.Current.HoverExpand && _stateMachine.CurrentState is IslandState.Idle or IslandState.Compact or IslandState.Minimal)
        {
            _hoverExpandTimer.Stop();
            _hoverExpandTimer.Start();
        }
    }

    private void OnPillMouseLeave(object sender, MouseEventArgs e)
    {
        _hoverExpandTimer.Stop();
        _longPressTimer.Stop();
        _isPointerDown = false;
        _animator.SetScaleTarget(1.0);
    }

    private void OnPillMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInteractiveChild(e.OriginalSource as DependencyObject)) return;

        _isPointerDown = true;
        _longPressTriggered = false;
        _pointerDownPos = e.GetPosition(this);
        _animator.SetScaleTarget(0.95);

        _longPressTimer.Stop();
        _longPressTimer.Start();
    }

    private void OnPillMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPointerDown) return;
        Point cur = e.GetPosition(this);
        if (Math.Abs(cur.X - _pointerDownPos.X) > 12)
        {
            _longPressTimer.Stop();
        }
    }

    private void OnPillMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPointerDown) return;
        _isPointerDown = false;
        _longPressTimer.Stop();
        _animator.SetScaleTarget(IsMouseOver ? 1.03 : 1.0);

        if (_longPressTriggered) return;

        Point upPos = e.GetPosition(this);
        double dx = upPos.X - _pointerDownPos.X;

        if (Math.Abs(dx) >= 34)
        {
            if (_stateMachine.CurrentState == IslandState.Alert)
            {
                _stateMachine.DismissAlert();
            }
            else if (_activityManager.Activities.Count > 1)
            {
                if (dx < 0) _activityManager.CycleNext();
                else _activityManager.CyclePrevious();
            }
            return;
        }

        if (Math.Abs(dx) < 12)
        {
            _stateMachine.ToggleExpand();
        }
    }

    private void OnPillRightClick(object sender, MouseButtonEventArgs e)
    {
        if (_stateMachine.CurrentState == IslandState.Expanded)
        {
            _stateMachine.Collapse();
        }
        else
        {
            _openSettingsAction();
        }
    }

    private void OnPillMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_settingsService.Current.EnableOsd) return;
        double delta = e.Delta > 0 ? 4.0 : -4.0;
        _audioService.AdjustVolume(delta);
        _osdModule.ShowVolumeHud(_audioService.VolumePercent, _audioService.IsMuted);
        e.Handled = true;
    }

    private void OnPillDragEnter(object sender, DragEventArgs e)
    {
        if (_settingsService.Current.EnableFileShelf && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            _animator.SetScaleTarget(1.06);
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnPillDrop(object sender, DragEventArgs e)
    {
        _animator.SetScaleTarget(1.0);
        if (_settingsService.Current.EnableFileShelf &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Length > 0)
        {
            _shelfModule.AddFiles(files);
        }
        e.Handled = true;
    }

    // --- Button & Interactive Control Handlers ---

    private void OnPrevActivityClick(object sender, RoutedEventArgs e)
    {
        _forceQuickActionsTab = false;
        _activityManager.CyclePrevious();
    }

    private void OnNextActivityClick(object sender, RoutedEventArgs e)
    {
        _forceQuickActionsTab = false;
        _activityManager.CycleNext();
    }

    private void OnQuickActionsTabClick(object sender, RoutedEventArgs e)
    {
        _forceQuickActionsTab = !_forceQuickActionsTab;
        if (_forceQuickActionsTab)
        {
            _ = _quickActionsModule.RefreshOnExpandAsync();
        }
        ApplyStateLayout(IslandState.Expanded, snapImmediately: false);
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e) => _openSettingsAction();
    private void OnCollapseClick(object sender, RoutedEventArgs e) => _stateMachine.Collapse();

    private void OnMediaPlayPauseClick(object sender, RoutedEventArgs e) => _ = _mediaModule.TogglePlayPauseAsync();
    private void OnMediaNextClick(object sender, RoutedEventArgs e) => _ = _mediaModule.NextTrackAsync();
    private void OnMediaPrevClick(object sender, RoutedEventArgs e) => _ = _mediaModule.PreviousTrackAsync();

    private void OnMediaSeekStart(object sender, MouseButtonEventArgs e) => _isSeekingMedia = true;
    private void OnMediaSeekCommit(object sender, MouseButtonEventArgs e)
    {
        _isSeekingMedia = false;
        _ = _mediaModule.SeekRatioAsync(MediaSeekSlider.Value);
    }

    private void OnQuickTimerPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string minsStr } && int.TryParse(minsStr, out int mins))
        {
            _forceQuickActionsTab = false;
            _timerModule.StartCountdown(TimeSpan.FromMinutes(mins), $"{mins}m Timer");
            ApplyStateLayout(_stateMachine.CurrentState, snapImmediately: false);
        }
    }

    private void OnStartStopwatchClick(object sender, RoutedEventArgs e)
    {
        _forceQuickActionsTab = false;
        _timerModule.StartStopwatch();
        ApplyStateLayout(_stateMachine.CurrentState, snapImmediately: false);
    }

    private void OnTimerPauseResumeClick(object sender, RoutedEventArgs e) => _timerModule.TogglePause();
    private void OnTimerStopClick(object sender, RoutedEventArgs e) => _timerModule.Stop();

    private void OnCallMuteClick(object sender, RoutedEventArgs e) => _callModule.ToggleCallMute();
    private void OnCallFocusOrEndClick(object sender, RoutedEventArgs e) => _callModule.FocusOrEndCall();

    private void OnAlertOsdSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || AlertPanel.Visibility != Visibility.Visible) return;
        if (Mouse.LeftButton != MouseButtonState.Pressed) return;
        if (_stateMachine.ActiveAlertActivity is OsdActivity osd)
        {
            osd.SetInteractiveValue(e.NewValue);
        }
    }

    private void OnRemoveShelfItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
        {
            _shelfModule.RemoveFile(path);
        }
    }

    private void OnClearShelfClick(object sender, RoutedEventArgs e) => _shelfModule.Clear();

    private void OnShelfItemMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (ShelfListBox.SelectedItem is ShelfFileItem item)
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { item.FullPath });
            DragDrop.DoDragDrop(ShelfListBox, data, DragDropEffects.Copy | DragDropEffects.Move);
        }
    }

    private void OnDragAllShelfItems(object sender, MouseButtonEventArgs e)
    {
        string[] paths = _shelfModule.GetExistingPaths();
        if (paths.Length > 0)
        {
            var data = new DataObject(DataFormats.FileDrop, paths);
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy | DragDropEffects.Move);
        }
    }

    private void OnDismissCurrentActivityClick(object sender, RoutedEventArgs e)
    {
        var primary = _activityManager.PrimaryActivity;
        if (primary is not null)
        {
            _activityManager.Remove(primary.Id);
        }
    }

    private void OnScreenshotClick(object sender, RoutedEventArgs e) => _quickActionsModule.TriggerScreenshot();
    private void OnToggleDndClick(object sender, RoutedEventArgs e) => _quickActionsModule.ToggleDoNotDisturb();

    private void OnClipboardItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ClipboardPreviewItem item })
        {
            _quickActionsModule.CopyClipboardItem(item);
            _stateMachine.Collapse();
        }
    }

    private static bool IsInteractiveChild(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase or Slider or ListBoxItem)
            {
                return true;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private static Color ParseAccent(string hex)
    {
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color c) return c;
        }
        catch { }
        return Color.FromRgb(10, 132, 255);
    }

    protected override void OnClosed(EventArgs e)
    {
        _longPressTimer.Stop();
        _longPressTimer.Tick -= OnLongPressTimerTick;
        _hoverExpandTimer.Stop();
        _hoverExpandTimer.Tick -= OnHoverExpandTimerTick;

        if (_observedActivity is not null)
        {
            _observedActivity.PropertyChanged -= OnObservedActivityPropertyChanged;
            _observedActivity = null;
        }

        _windowController.DisplayOrDpiChanged -= OnDisplayOrDpiChanged;
        _privacyModule.PrivacyDotsChanged -= OnPrivacyDotsChanged;
        _quickActionsModule.StateUpdated -= OnQuickActionsOrGlanceUpdated;
        _glanceModule.GlanceUpdated -= OnQuickActionsOrGlanceUpdated;
        _animator.FrameUpdated -= OnAnimatorFrameUpdated;
        _animator.Settled -= OnAnimatorSettled;
        _stateMachine.StateChanged -= OnIslandStateChanged;
        _activityManager.ActivitiesChanged -= OnActivitiesChanged;
        _settingsService.SettingsChanged -= OnSettingsChanged;
        _fullscreenDetector.FullscreenChanged -= OnFullscreenChanged;
        _windowController.Dispose();
        base.OnClosed(e);
    }
}

