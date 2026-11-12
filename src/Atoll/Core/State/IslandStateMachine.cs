using Atoll.Core.Activities;

namespace Atoll.Core.State;

public sealed class IslandStateChangedEventArgs : EventArgs
{
    public IslandState PreviousState { get; }
    public IslandState NewState { get; }
    public IIslandActivity? AlertActivity { get; }

    public IslandStateChangedEventArgs(IslandState previousState, IslandState newState, IIslandActivity? alertActivity = null)
    {
        PreviousState = previousState;
        NewState = newState;
        AlertActivity = alertActivity;
    }
}

public sealed class IslandStateMachine
{
    private readonly ActivityManager _activityManager;
    private readonly Action<TimeSpan, Action>? _scheduleTimer;
    private readonly Action? _cancelTimer;
    private int _alertToken;

    public IslandState CurrentState { get; private set; } = IslandState.Idle;
    public IIslandActivity? ActiveAlertActivity { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsFullscreenSuppressed { get; private set; }

    public event EventHandler<IslandStateChangedEventArgs>? StateChanged;

    public IslandStateMachine(
        ActivityManager activityManager,
        Action<TimeSpan, Action>? scheduleTimer = null,
        Action? cancelTimer = null)
    {
        _activityManager = activityManager;
        _scheduleTimer = scheduleTimer;
        _cancelTimer = cancelTimer;
        _activityManager.ActivitiesChanged += OnActivitiesChanged;
    }

    public IslandState ComputeNaturalRestState()
    {
        int count = _activityManager.Activities.Count;
        if (count <= 0) return IslandState.Idle;
        if (count == 1) return IslandState.Compact;
        return IslandState.Minimal;
    }

    public void Expand()
    {
        if (IsPaused) return;
        CancelPendingAlert();
        TransitionTo(IslandState.Expanded, null);
    }

    public void Collapse()
    {
        CancelPendingAlert();
        TransitionTo(ComputeNaturalRestState(), null);
    }

    public void ToggleExpand()
    {
        if (CurrentState == IslandState.Expanded)
        {
            Collapse();
        }
        else
        {
            Expand();
        }
    }

    public void ShowAlert(IIslandActivity alertActivity, TimeSpan duration)
    {
        if (IsPaused) return;

        // If user currently has Expanded open and a lower-priority alert arrives, keep Expanded unless High/Critical
        if (CurrentState == IslandState.Expanded && alertActivity.Priority < ActivityPriority.High)
        {
            return;
        }

        int token = ++_alertToken;
        ActiveAlertActivity = alertActivity;
        TransitionTo(IslandState.Alert, alertActivity);

        if (_scheduleTimer is not null)
        {
            _scheduleTimer(duration, () =>
            {
                if (token == _alertToken && CurrentState == IslandState.Alert)
                {
                    ActiveAlertActivity = null;
                    TransitionTo(ComputeNaturalRestState(), null);
                }
            });
        }
    }

    public void DismissAlert()
    {
        if (CurrentState != IslandState.Alert) return;

        var dismissed = ActiveAlertActivity;
        CancelPendingAlert();
        dismissed?.OnDismissed();
        TransitionTo(ComputeNaturalRestState(), null);
    }

    public void SetPaused(bool paused)
    {
        if (IsPaused == paused) return;
        IsPaused = paused;
        if (paused)
        {
            CancelPendingAlert();
            TransitionTo(IslandState.Idle, null);
        }
        else
        {
            TransitionTo(ComputeNaturalRestState(), null);
        }
    }

    public void SetFullscreenSuppressed(bool suppressed)
    {
        if (IsFullscreenSuppressed == suppressed) return;
        IsFullscreenSuppressed = suppressed;
        if (suppressed && CurrentState == IslandState.Expanded)
        {
            Collapse();
        }
    }

    private void OnActivitiesChanged(object? sender, EventArgs e)
    {
        if (IsPaused) return;

        if (CurrentState is IslandState.Idle or IslandState.Compact or IslandState.Minimal)
        {
            TransitionTo(ComputeNaturalRestState(), null);
        }
    }

    private void CancelPendingAlert()
    {
        _alertToken++;
        ActiveAlertActivity = null;
        _cancelTimer?.Invoke();
    }

    private void TransitionTo(IslandState nextState, IIslandActivity? alertActivity)
    {
        if (CurrentState == nextState && !ReferenceEquals(alertActivity, ActiveAlertActivity))
        {
            StateChanged?.Invoke(this, new IslandStateChangedEventArgs(CurrentState, nextState, alertActivity));
            return;
        }

        if (CurrentState == nextState && nextState != IslandState.Alert)
        {
            // Still notify if natural layout needs refresh (e.g. Compact content updated)
            StateChanged?.Invoke(this, new IslandStateChangedEventArgs(CurrentState, nextState, alertActivity));
            return;
        }

        var prev = CurrentState;
        CurrentState = nextState;
        StateChanged?.Invoke(this, new IslandStateChangedEventArgs(prev, nextState, alertActivity));
    }
}

