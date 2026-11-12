using Atoll.Core.Activities;
using Xunit;

namespace Atoll.Tests;

public sealed class ActivityManagerTests
{
    [Fact]
    public void PriorityOrdering_PlacesHighestPriorityFirst()
    {
        var mgr = new ActivityManager();
        var low = new FakeActivity("low", "privacy", ActivityPriority.Low);
        var normal = new FakeActivity("norm", "media", ActivityPriority.Normal);
        var critical = new FakeActivity("crit", "calls", ActivityPriority.Critical);

        mgr.RegisterOrUpdate(low);
        mgr.RegisterOrUpdate(normal);
        mgr.RegisterOrUpdate(critical);

        Assert.Same(critical, mgr.PrimaryActivity);
        Assert.Same(normal, mgr.SecondaryActivity);
    }

    [Fact]
    public void CycleNextAndPrevious_SwitchesPrimaryActivity()
    {
        var mgr = new ActivityManager();
        var a1 = new FakeActivity("a1", "m1", ActivityPriority.High);
        var a2 = new FakeActivity("a2", "m2", ActivityPriority.Normal);
        var a3 = new FakeActivity("a3", "m3", ActivityPriority.Low);

        mgr.RegisterOrUpdate(a1);
        mgr.RegisterOrUpdate(a2);
        mgr.RegisterOrUpdate(a3);

        Assert.Same(a1, mgr.PrimaryActivity);

        mgr.CycleNext();
        Assert.Same(a2, mgr.PrimaryActivity);

        mgr.CycleNext();
        Assert.Same(a3, mgr.PrimaryActivity);

        mgr.CyclePrevious();
        Assert.Same(a2, mgr.PrimaryActivity);
    }

    [Fact]
    public void RemoveByModule_ClearsOnlyMatchingActivities()
    {
        var mgr = new ActivityManager();
        mgr.RegisterOrUpdate(new FakeActivity("m1.1", "mod1"));
        mgr.RegisterOrUpdate(new FakeActivity("m1.2", "mod1"));
        mgr.RegisterOrUpdate(new FakeActivity("m2.1", "mod2"));

        mgr.RemoveByModule("mod1");
        Assert.Single(mgr.Activities);
        Assert.Equal("m2.1", mgr.PrimaryActivity?.Id);
    }
}

