using Atoll.Core.Activities;
using Atoll.Core.State;
using Xunit;

namespace Atoll.Tests;

internal sealed class FakeActivity : IslandActivityBase
{
    public override ActivityKind Kind => ActivityKind.Generic;
    public bool DismissedCalled { get; private set; }

    public FakeActivity(string id, string moduleId, ActivityPriority priority = ActivityPriority.Normal)
        : base(id, moduleId)
    {
        Priority = priority;
        Title = id;
    }

    public override void OnDismissed() => DismissedCalled = true;
}

public sealed class IslandStateMachineTests
{
    [Fact]
    public void StateMachine_AutoTransitions_IdleCompactMinimal()
    {
        var manager = new ActivityManager();
        var sm = new IslandStateMachine(manager);

        Assert.Equal(IslandState.Idle, sm.CurrentState);

        var a1 = new FakeActivity("a1", "m1");
        manager.RegisterOrUpdate(a1);
        Assert.Equal(IslandState.Compact, sm.CurrentState);

        var a2 = new FakeActivity("a2", "m2");
        manager.RegisterOrUpdate(a2);
        Assert.Equal(IslandState.Minimal, sm.CurrentState);

        manager.Remove("a1");
        Assert.Equal(IslandState.Compact, sm.CurrentState);

        manager.Remove("a2");
        Assert.Equal(IslandState.Idle, sm.CurrentState);
    }

    [Fact]
    public void StateMachine_AlertAutoCollapses_ToNaturalRestState()
    {
        var manager = new ActivityManager();
        Action? pending = null;
        var sm = new IslandStateMachine(
            manager,
            scheduleTimer: (_, cb) => pending = cb,
            cancelTimer: () => pending = null);

        manager.RegisterOrUpdate(new FakeActivity("media", "media"));
        Assert.Equal(IslandState.Compact, sm.CurrentState);

        var alert = new FakeActivity("notif.1", "notifications", ActivityPriority.High);
        sm.ShowAlert(alert, TimeSpan.FromSeconds(3));
        Assert.Equal(IslandState.Alert, sm.CurrentState);
        Assert.Same(alert, sm.ActiveAlertActivity);

        Assert.NotNull(pending);
        pending!.Invoke();

        Assert.Equal(IslandState.Compact, sm.CurrentState);
        Assert.Null(sm.ActiveAlertActivity);
    }

    [Fact]
    public void StateMachine_DismissAlert_InvokesOnDismissedAndRestoresRestState()
    {
        var manager = new ActivityManager();
        var sm = new IslandStateMachine(manager);
        var alert = new FakeActivity("notif.2", "notifications", ActivityPriority.High);

        sm.ShowAlert(alert, TimeSpan.FromSeconds(4));
        Assert.Equal(IslandState.Alert, sm.CurrentState);

        sm.DismissAlert();
        Assert.True(alert.DismissedCalled);
        Assert.Equal(IslandState.Idle, sm.CurrentState);
    }

    [Fact]
    public void StateMachine_Pause_ForcesIdleAndBlocksExpand()
    {
        var manager = new ActivityManager();
        var sm = new IslandStateMachine(manager);
        manager.RegisterOrUpdate(new FakeActivity("media", "media"));

        sm.SetPaused(true);
        Assert.Equal(IslandState.Idle, sm.CurrentState);

        sm.Expand();
        Assert.Equal(IslandState.Idle, sm.CurrentState);

        sm.SetPaused(false);
        Assert.Equal(IslandState.Compact, sm.CurrentState);
    }

    [Fact]
    public void StateMachine_Dispose_DetachesFromActivityManager()
    {
        var manager = new ActivityManager();
        var sm = new IslandStateMachine(manager);
        int stateEvents = 0;
        sm.StateChanged += (_, _) => stateEvents++;

        sm.Dispose();
        manager.RegisterOrUpdate(new FakeActivity("media", "media"));

        Assert.Equal(0, stateEvents);
        Assert.Equal(IslandState.Idle, sm.CurrentState);
    }
}

