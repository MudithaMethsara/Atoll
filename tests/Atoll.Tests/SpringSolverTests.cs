using Atoll.Core.Animation;
using Xunit;

namespace Atoll.Tests;

public sealed class SpringSolverTests
{
    [Fact]
    public void Spring_ConvergesToTarget_WithinEpsilon()
    {
        var spring = new SpringSolver(initialValue: 120, stiffness: 360, dampingRatio: 0.76);
        spring.SetTarget(380);

        for (int i = 0; i < 300; i++)
        {
            if (!spring.Step(1.0 / 60.0)) break;
        }

        Assert.True(spring.IsSettled);
        Assert.Equal(380.0, spring.Position, precision: 2);
        Assert.Equal(0.0, spring.Velocity, precision: 2);
    }

    [Fact]
    public void Spring_MidFlightTargetChange_PreservesPositionAndVelocity()
    {
        var spring = new SpringSolver(initialValue: 120, stiffness: 340, dampingRatio: 0.75);
        spring.SetTarget(380);

        for (int i = 0; i < 5; i++)
        {
            spring.Step(1.0 / 60.0);
        }

        double posBefore = spring.Position;
        double velBefore = spring.Velocity;
        Assert.True(posBefore > 120.0);
        Assert.True(velBefore > 0.0);

        // Reverse target mid-flight
        spring.SetTarget(120);
        Assert.Equal(posBefore, spring.Position);
        Assert.Equal(velBefore, spring.Velocity);

        for (int i = 0; i < 300; i++)
        {
            if (!spring.Step(1.0 / 60.0)) break;
        }

        Assert.True(spring.IsSettled);
        Assert.Equal(120.0, spring.Position, precision: 2);
    }

    [Fact]
    public void Spring_Snap_ImmediatelySettles()
    {
        var spring = new SpringSolver(initialValue: 100);
        spring.SetTarget(300);
        spring.Step(1.0 / 60.0);
        Assert.False(spring.IsSettled);

        spring.Snap(250);
        Assert.True(spring.IsSettled);
        Assert.Equal(250.0, spring.Position);
        Assert.Equal(0.0, spring.Velocity);
    }
}

