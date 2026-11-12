using Atoll.Core.Animation;
using Xunit;

namespace Atoll.Tests;

public sealed class MorphAnimatorTests
{
    [Fact]
    public void SnapAll_UpdatesCurrentFrameAndStopsAnimation()
    {
        StaTestHelper.Run(() =>
        {
            using var animator = new MorphAnimator();
            MorphFrame? received = null;
            bool settled = false;

            animator.FrameUpdated += f => received = f;
            animator.Settled += () => settled = true;

            animator.SnapAll(240, 44, 22, 12, bubbleSize: 36, bubbleGap: 8, bubbleOpacity: 1.0, pillOpacity: 0.9);

            Assert.NotNull(received);
            Assert.True(settled);
            Assert.False(animator.IsAnimating);
            Assert.Equal(240.0, received!.Value.Width, precision: 2);
            Assert.Equal(44.0, received.Value.Height, precision: 2);
            Assert.Equal(22.0, received.Value.CornerRadius, precision: 2);
            Assert.Equal(12.0, received.Value.TopOffset, precision: 2);
            Assert.Equal(36.0, received.Value.BubbleSize, precision: 2);
            Assert.Equal(8.0, received.Value.BubbleGap, precision: 2);
        });
    }

    [Fact]
    public void SetLayoutTargets_StartsAnimationLoopUntilDisposed()
    {
        StaTestHelper.Run(() =>
        {
            using var animator = new MorphAnimator
            {
                SpeedMultiplier = 1.5,
                LowPowerMode = true
            };

            animator.SetLayoutTargets(360, 160, 28, 8);
            Assert.True(animator.IsAnimating);

            animator.SetScaleTarget(0.95, impulseVelocity: -0.2);
            Assert.True(animator.IsAnimating);
        });
    }
}

