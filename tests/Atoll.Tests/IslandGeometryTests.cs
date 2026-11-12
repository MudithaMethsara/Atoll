using System.Windows;
using Atoll.Core.Animation;
using Xunit;

namespace Atoll.Tests;

public sealed class IslandGeometryTests
{
    [Fact]
    public void CreateSquircle_ReturnsFrozenGeometryWithExpectedBounds()
    {
        var geo = IslandGeometry.CreateSquircle(10, 20, 120, 34, 17);

        Assert.True(geo.IsFrozen);
        Assert.False(geo.IsEmpty());
        Assert.Equal(10.0, geo.Bounds.Left, precision: 1);
        Assert.Equal(20.0, geo.Bounds.Top, precision: 1);
        Assert.Equal(120.0, geo.Bounds.Width, precision: 1);
        Assert.Equal(34.0, geo.Bounds.Height, precision: 1);
    }

    [Fact]
    public void CreateSquircle_DegenerateZeroSize_ReturnsFrozenEmptyGeometry()
    {
        var geo = IslandGeometry.CreateSquircle(0, 0, 0, 0, 10);
        Assert.True(geo.IsFrozen);
        Assert.True(geo.IsEmpty());
    }

    [Fact]
    public void HitTestRoundedRect_RespectsPillInteriorAndRoundedCorners()
    {
        var rect = new Rect(100, 10, 120, 40);
        double radius = 20;

        // Center point is inside
        Assert.True(IslandGeometry.HitTestRoundedRect(new Point(160, 30), rect, radius));

        // Outside bounding box
        Assert.False(IslandGeometry.HitTestRoundedRect(new Point(95, 30), rect, radius));
        Assert.False(IslandGeometry.HitTestRoundedRect(new Point(160, 55), rect, radius));

        // Top-left corner square (101, 11) is inside bounding Rect, but OUTSIDE circular corner of radius 20
        Assert.False(IslandGeometry.HitTestRoundedRect(new Point(101, 11), rect, radius));

        // Point just inside rounded corner arc
        Assert.True(IslandGeometry.HitTestRoundedRect(new Point(112, 20), rect, radius));
    }

    [Fact]
    public void SquircleGeometryCache_ReusesGeometryWithinToleranceAndRebuildsOnChange()
    {
        var cache = new SquircleGeometryCache();
        var g1 = cache.GetOrCreate(10, 8, 120, 34, 17);
        var g2 = cache.GetOrCreate(10.02, 8.01, 120.01, 34, 17);
        var g3 = cache.GetOrCreate(10, 8, 220, 36, 18);

        Assert.Same(g1, g2);
        Assert.NotSame(g1, g3);
    }
}

