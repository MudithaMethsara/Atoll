using System.Windows;
using Atoll.Core.Window;
using Xunit;

namespace Atoll.Tests;

public sealed class DisplayMonitorServiceTests
{
    [Fact]
    public void GetMonitors_ReturnsAtLeastOneMonitor_WithValidDpi()
    {
        var svc = new DisplayMonitorService();
        var monitors = svc.GetMonitors();

        Assert.NotEmpty(monitors);
        Assert.Contains(monitors, m => m.IsPrimary);
        Assert.All(monitors, m =>
        {
            Assert.True(m.DpiX >= 96);
            Assert.True(m.PhysicalBounds.Width > 0);
            Assert.True(m.PhysicalBounds.Height > 0);
        });
    }

    [Fact]
    public void ResolveTargetMonitor_FallsBackToPrimaryWhenNameUnknown()
    {
        var svc = new DisplayMonitorService();
        var resolved = svc.ResolveTargetMonitor(@"\\.\DISPLAY_NONEXISTENT_99");
        Assert.True(resolved.IsPrimary || svc.GetMonitors().Count == 1);
    }

    [Fact]
    public void ComputeOverlayPhysicalRect_CentersHorizontallyAndAppliesDpiScale()
    {
        var svc = new DisplayMonitorService();
        var monitor = new MonitorDescriptor(
            IntPtr.Zero,
            @"\\.\DISPLAY1",
            true,
            new Rect(0, 0, 3840, 2160),
            new Rect(0, 0, 3840, 2100),
            192, // 2.0x DPI scale
            192);

        var rect = svc.ComputeOverlayPhysicalRect(monitor, 580, 310, horizontalOffsetDip: 20);

        Assert.Equal(1160, rect.Width);
        Assert.Equal(620, rect.Height);
        // Centered: (3840 - 1160) / 2 = 1340, plus 20 * 2.0 = 1380
        Assert.Equal(1380, rect.Left);
        Assert.Equal(0, rect.Top);
    }
}

