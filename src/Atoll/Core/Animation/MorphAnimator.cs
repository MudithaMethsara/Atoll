using System.Diagnostics;
using System.Windows.Media;

namespace Atoll.Core.Animation;

public readonly record struct MorphFrame(
    double Width,
    double Height,
    double CornerRadius,
    double TopOffset,
    double PressScale,
    double ContentOpacity,
    double ContentScale,
    double BubbleSize,
    double BubbleGap,
    double BubbleOpacity,
    double PillOpacity);

public sealed class MorphAnimator : IDisposable
{
    private readonly SpringSolver _width = new(120, stiffness: 360, dampingRatio: 0.76);
    private readonly SpringSolver _height = new(34, stiffness: 360, dampingRatio: 0.76);
    private readonly SpringSolver _cornerRadius = new(17, stiffness: 360, dampingRatio: 0.78);
    private readonly SpringSolver _topOffset = new(8, stiffness: 320, dampingRatio: 0.78);
    private readonly SpringSolver _pressScale = new(1.0, stiffness: 520, dampingRatio: 0.70, positionEpsilon: 0.002, velocityEpsilon: 0.01);
    private readonly SpringSolver _contentOpacity = new(1.0, stiffness: 280, dampingRatio: 0.85, positionEpsilon: 0.005, velocityEpsilon: 0.02);
    private readonly SpringSolver _contentScale = new(1.0, stiffness: 340, dampingRatio: 0.78, positionEpsilon: 0.002, velocityEpsilon: 0.01);
    private readonly SpringSolver _bubbleSize = new(0.0, stiffness: 340, dampingRatio: 0.74);
    private readonly SpringSolver _bubbleGap = new(0.0, stiffness: 340, dampingRatio: 0.74);
    private readonly SpringSolver _bubbleOpacity = new(0.0, stiffness: 300, dampingRatio: 0.85, positionEpsilon: 0.005, velocityEpsilon: 0.02);
    private readonly SpringSolver _pillOpacity = new(1.0, stiffness: 260, dampingRatio: 0.88, positionEpsilon: 0.005, velocityEpsilon: 0.02);

    private readonly SpringSolver[] _allSprings;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTimestampSec;
    private double _lastRenderSec;
    private bool _isRenderingSubscribed;
    private bool _disposed;

    public double SpeedMultiplier { get; set; } = 1.0;
    public bool LowPowerMode { get; set; } = false;
    public int ActiveSpringCount { get; private set; }
    public double LastFps { get; private set; }
    public bool IsAnimating => _isRenderingSubscribed;

    public event Action<MorphFrame>? FrameUpdated;
    public event Action? Settled;

    public MorphAnimator()
    {
        _allSprings =
        [
            _width,
            _height,
            _cornerRadius,
            _topOffset,
            _pressScale,
            _contentOpacity,
            _contentScale,
            _bubbleSize,
            _bubbleGap,
            _bubbleOpacity,
            _pillOpacity
        ];
    }

    public MorphFrame CurrentFrame => new(
        Width: Math.Max(20, _width.Position),
        Height: Math.Max(16, _height.Position),
        CornerRadius: Math.Max(4, _cornerRadius.Position),
        TopOffset: Math.Max(0, _topOffset.Position),
        PressScale: Math.Clamp(_pressScale.Position, 0.82, 1.18),
        ContentOpacity: Math.Clamp(_contentOpacity.Position, 0.0, 1.0),
        ContentScale: Math.Clamp(_contentScale.Position, 0.80, 1.15),
        BubbleSize: Math.Max(0, _bubbleSize.Position),
        BubbleGap: Math.Max(0, _bubbleGap.Position),
        BubbleOpacity: Math.Clamp(_bubbleOpacity.Position, 0.0, 1.0),
        PillOpacity: Math.Clamp(_pillOpacity.Position, 0.0, 1.0));

    public void SetLayoutTargets(
        double width,
        double height,
        double cornerRadius,
        double topOffset,
        double bubbleSize = 0.0,
        double bubbleGap = 0.0,
        double bubbleOpacity = 0.0,
        double pillOpacity = 1.0,
        bool triggerContentStagger = true)
    {
        _width.SetTarget(width);
        _height.SetTarget(height);
        _cornerRadius.SetTarget(cornerRadius);
        _topOffset.SetTarget(topOffset);
        _bubbleSize.SetTarget(bubbleSize);
        _bubbleGap.SetTarget(bubbleGap);
        _bubbleOpacity.SetTarget(bubbleOpacity);
        _pillOpacity.SetTarget(pillOpacity);

        if (triggerContentStagger)
        {
            _contentOpacity.Snap(0.15);
            _contentOpacity.SetTarget(1.0);
            _contentScale.Snap(0.93);
            _contentScale.SetTarget(1.0);
        }

        EnsureRunning();
    }

    public void SetScaleTarget(double scale, double impulseVelocity = 0.0)
    {
        _pressScale.SetTarget(scale);
        if (Math.Abs(impulseVelocity) > 0.001)
        {
            _pressScale.Impulse(impulseVelocity);
        }
        EnsureRunning();
    }

    public void SnapAll(
        double width,
        double height,
        double cornerRadius,
        double topOffset,
        double bubbleSize = 0.0,
        double bubbleGap = 0.0,
        double bubbleOpacity = 0.0,
        double pillOpacity = 1.0)
    {
        _width.Snap(width);
        _height.Snap(height);
        _cornerRadius.Snap(cornerRadius);
        _topOffset.Snap(topOffset);
        _pressScale.Snap(1.0);
        _contentOpacity.Snap(1.0);
        _contentScale.Snap(1.0);
        _bubbleSize.Snap(bubbleSize);
        _bubbleGap.Snap(bubbleGap);
        _bubbleOpacity.Snap(bubbleOpacity);
        _pillOpacity.Snap(pillOpacity);

        StopLoop();
        FrameUpdated?.Invoke(CurrentFrame);
        Settled?.Invoke();
    }

    private void EnsureRunning()
    {
        if (_disposed) return;
        if (_isRenderingSubscribed) return;

        _lastTimestampSec = _clock.Elapsed.TotalSeconds;
        _lastRenderSec = _lastTimestampSec;
        _isRenderingSubscribed = true;
        CompositionTarget.Rendering += OnCompositionTargetRendering;
    }

    private void StopLoop()
    {
        if (!_isRenderingSubscribed) return;
        CompositionTarget.Rendering -= OnCompositionTargetRendering;
        _isRenderingSubscribed = false;
        ActiveSpringCount = 0;
        LastFps = 0;
    }

    private void OnCompositionTargetRendering(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            StopLoop();
            return;
        }

        double now = _clock.Elapsed.TotalSeconds;
        if (LowPowerMode && (now - _lastRenderSec) < (1.0 / 32.0))
        {
            return;
        }

        double dt = now - _lastTimestampSec;
        _lastTimestampSec = now;
        _lastRenderSec = now;

        if (dt > 0.0001)
        {
            LastFps = 1.0 / dt;
        }

        double scaledDt = dt * Math.Clamp(SpeedMultiplier, 0.5, 2.0);
        bool anyActive = false;
        int activeCount = 0;

        for (int i = 0; i < _allSprings.Length; i++)
        {
            if (_allSprings[i].Step(scaledDt))
            {
                anyActive = true;
                activeCount++;
            }
        }

        ActiveSpringCount = activeCount;
        FrameUpdated?.Invoke(CurrentFrame);

        if (!anyActive)
        {
            StopLoop();
            Settled?.Invoke();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        StopLoop();
    }
}

