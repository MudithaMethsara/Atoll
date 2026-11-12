using System.IO;
using Atoll.Modules.Timer;
using Xunit;

namespace Atoll.Tests;

public sealed class TimerModuleTests
{
    [Fact]
    public void TimerModule_StartPauseStop_ManagesActivityAndProgress()
    {
        string path = Path.Combine(Path.GetTempPath(), $"atoll_timer_{Guid.NewGuid():N}.json");
        try
        {
            StaTestHelper.Run(() =>
            {
                var ctx = StaTestHelper.CreateTestModuleContext(path);
                using var module = new TimerModule();
                module.InitializeAsync(ctx).GetAwaiter().GetResult();

                Assert.False(module.IsActive);

                module.StartCountdown(TimeSpan.FromMinutes(5), "Focus");
                Assert.True(module.IsActive);
                var activity = Assert.IsType<TimerActivity>(ctx.ActivityManager.PrimaryActivity);
                Assert.True(activity.IsRunning);
                Assert.False(activity.IsStopwatch);
                Assert.Equal("05:00", activity.FormattedTime);

                module.TogglePause();
                Assert.False(activity.IsRunning);

                module.TogglePause();
                Assert.True(activity.IsRunning);

                module.StartStopwatch();
                Assert.True(activity.IsStopwatch);

                module.Stop();
                Assert.False(module.IsActive);
                Assert.Null(ctx.ActivityManager.PrimaryActivity);

                ctx.AudioService.Dispose();
                ctx.BrightnessService.Dispose();
            });
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

