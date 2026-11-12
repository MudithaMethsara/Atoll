using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Threading;
using Atoll.Core.Activities;
using Atoll.Core.Animation;
using Atoll.Core.State;
using Atoll.Core.Window;

namespace Atoll.Views;

public partial class DiagnosticsOverlay : System.Windows.Window
{
    private readonly IslandStateMachine _stateMachine;
    private readonly MorphAnimator _animator;
    private readonly ActivityManager _activityManager;
    private readonly DispatcherTimer _timer;
    private readonly Process _process = Process.GetCurrentProcess();
    private TimeSpan _lastCpuTime;
    private DateTime _lastSampleUtc;

    public DiagnosticsOverlay(
        IslandStateMachine stateMachine,
        MorphAnimator animator,
        ActivityManager activityManager)
    {
        InitializeComponent();
        _stateMachine = stateMachine;
        _animator = animator;
        _activityManager = activityManager;

        _lastCpuTime = _process.TotalProcessorTime;
        _lastSampleUtc = DateTime.UtcNow;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Win32Interop.EnableDarkWindowFrame(new WindowInteropHelper(this).Handle, rounded: true);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _process.Refresh();
        var now = DateTime.UtcNow;
        var cpuNow = _process.TotalProcessorTime;
        double elapsedMs = (now - _lastSampleUtc).TotalMilliseconds;
        double cpuUsedMs = (cpuNow - _lastCpuTime).TotalMilliseconds;
        double cpuPercent = elapsedMs > 1 ? (cpuUsedMs / (elapsedMs * Environment.ProcessorCount)) * 100.0 : 0.0;

        _lastCpuTime = cpuNow;
        _lastSampleUtc = now;

        double workingSetMb = _process.WorkingSet64 / (1024.0 * 1024.0);
        double managedMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);

        StateText.Text = $"State: {_stateMachine.CurrentState} | Springs: {_animator.ActiveSpringCount}";
        FpsText.Text = _animator.IsAnimating ? $"Anim FPS: {_animator.LastFps:F0}" : "Anim FPS: 0 (Idle Sleep)";
        CpuText.Text = $"CPU: {cpuPercent:F2}%";
        RamText.Text = $"RAM: {workingSetMb:F1} MB (GC: {managedMb:F1} MB)";
        ModulesText.Text = _activityManager.Activities.Count == 0
            ? "Activities: none"
            : "Activities: " + string.Join(", ", _activityManager.Activities.Select(a => a.ModuleId));
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _process.Dispose();
        base.OnClosed(e);
    }
}

