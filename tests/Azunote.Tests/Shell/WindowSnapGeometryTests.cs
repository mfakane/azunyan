using Xunit;

namespace Azunote.Tests.Shell;

public class WindowSnapGeometryTests
{
    [Theory]
    [InlineData(9, 10, false)]
    [InlineData(10, 10, false)]
    [InlineData(11, 10, true)]
    public void IsFastMovement_compares_distance_with_elapsed_time(
        int horizontalDistance,
        int elapsedMilliseconds,
        bool expected)
    {
        Assert.Equal(expected, WindowSnapGeometry.IsFastMovement(
            new WindowSnapPoint(0, 0),
            new WindowSnapPoint(horizontalDistance, 0),
            TimeSpan.FromMilliseconds(elapsedMilliseconds),
            1000));
    }

    [Theory]
    [InlineData(3, 4, 5, true)]
    [InlineData(3, 4, 6, false)]
    public void HasMovedAtLeast_uses_total_distance_from_snap_origin(
        int horizontalDistance,
        int verticalDistance,
        int releaseDistance,
        bool expected)
    {
        Assert.Equal(expected, WindowSnapGeometry.HasMovedAtLeast(
            new WindowSnapPoint(10, 10),
            new WindowSnapPoint(10 + horizontalDistance, 10 + verticalDistance),
            releaseDistance));
    }

    [Fact]
    public void Release_tracker_rearms_after_cursor_leaves_the_snap_zone()
    {
        var tracker = new WindowSnapReleaseTracker(10, 16);
        tracker.BeginDrag(new WindowSnapPoint(100, 100));
        tracker.ObserveSnap(
            new WindowSnapPoint(100, 100),
            new WindowSnapBounds(200, 200, 300, 300));

        Assert.False(tracker.TryRelease(new WindowSnapPoint(109, 100), out _));
        Assert.True(tracker.TryRelease(new WindowSnapPoint(110, 100), out var released));
        Assert.Equal(new WindowSnapBounds(210, 200, 310, 300), released);
        Assert.True(tracker.TryRelease(new WindowSnapPoint(116, 100), out _));
        Assert.False(tracker.TryRelease(new WindowSnapPoint(117, 100), out _));
    }

    [Fact]
    public void Release_tracker_measures_first_snap_from_drag_start()
    {
        var tracker = new WindowSnapReleaseTracker(10, 16);
        tracker.BeginDrag(new WindowSnapPoint(100, 100));
        tracker.ObserveSnap(
            new WindowSnapPoint(102, 100),
            new WindowSnapBounds(202, 200, 302, 300));

        Assert.True(tracker.TryRelease(new WindowSnapPoint(110, 100), out var released));
        Assert.Equal(new WindowSnapBounds(210, 200, 310, 300), released);
    }

    [Theory]
    [InlineData(0, 10, 8, 20, 2, 10, 10, 20)]
    [InlineData(22, 10, 30, 20, 20, 10, 28, 20)]
    [InlineData(10, 0, 20, 8, 10, 2, 20, 10)]
    [InlineData(10, 22, 20, 30, 10, 20, 20, 28)]
    public void Snap_aligns_in_each_direction(int left, int top, int right, int bottom, int expectedLeft, int expectedTop, int expectedRight, int expectedBottom)
    {
        var result = WindowSnapGeometry.Snap(
            new WindowSnapBounds(left, top, right, bottom),
            [new WindowSnapBounds(10, 10, 20, 20)],
            10);

        Assert.Equal(new WindowSnapBounds(expectedLeft, expectedTop, expectedRight, expectedBottom), result);
    }

    [Fact]
    public void Snap_accepts_distance_at_threshold_and_preserves_size()
    {
        var result = WindowSnapGeometry.Snap(
            new WindowSnapBounds(0, 0, 10, 10),
            [new WindowSnapBounds(12, 0, 22, 10)],
            2);

        Assert.Equal(new WindowSnapBounds(2, 0, 12, 10), result);
    }

    [Fact]
    public void Snap_ignores_distance_beyond_threshold()
    {
        var moving = new WindowSnapBounds(0, 0, 10, 10);

        Assert.Equal(moving, WindowSnapGeometry.Snap(
            moving,
            [new WindowSnapBounds(13, 0, 23, 10)],
            2));
    }

    [Fact]
    public void Snap_does_not_use_targets_without_orthogonal_overlap()
    {
        var moving = new WindowSnapBounds(0, 0, 10, 10);

        Assert.Equal(moving, WindowSnapGeometry.Snap(moving, [new WindowSnapBounds(12, 10, 22, 20)], 2));
    }

    [Fact]
    public void Snap_chooses_closest_adjustment()
    {
        var result = WindowSnapGeometry.Snap(
            new WindowSnapBounds(0, 0, 10, 10),
            [new WindowSnapBounds(13, 0, 23, 10), new WindowSnapBounds(11, 0, 21, 10)],
            5);

        Assert.Equal(new WindowSnapBounds(1, 0, 11, 10), result);
    }

    [Fact]
    public void Snap_keeps_first_candidate_on_tie()
    {
        var result = WindowSnapGeometry.Snap(
            new WindowSnapBounds(0, 0, 10, 10),
            [new WindowSnapBounds(12, 0, 22, 10), new WindowSnapBounds(-2, 0, 8, 10)],
            2);

        Assert.Equal(new WindowSnapBounds(2, 0, 12, 10), result);
    }

    [Fact]
    public void Snap_can_adjust_both_axes()
    {
        var result = WindowSnapGeometry.Snap(
            new WindowSnapBounds(0, 0, 10, 10),
            [new WindowSnapBounds(9, 9, 19, 19)],
            2);

        Assert.Equal(new WindowSnapBounds(-1, -1, 9, 9), result);
    }

    [Theory]
    [InlineData(0, 3, 10, 13, 12, 0, 22, 10, 2, 0, 12, 10)]
    [InlineData(0, -3, 10, 7, 12, 0, 22, 10, 2, 0, 12, 10)]
    [InlineData(3, 0, 13, 10, 0, 12, 10, 22, 0, 2, 10, 12)]
    [InlineData(-3, 0, 7, 10, 0, 12, 10, 22, 0, 2, 10, 12)]
    public void Snap_aligns_parallel_edges_when_windows_are_adjacent(
        int left,
        int top,
        int right,
        int bottom,
        int targetLeft,
        int targetTop,
        int targetRight,
        int targetBottom,
        int expectedLeft,
        int expectedTop,
        int expectedRight,
        int expectedBottom)
    {
        var result = WindowSnapGeometry.Snap(
            new WindowSnapBounds(left, top, right, bottom),
            [new WindowSnapBounds(targetLeft, targetTop, targetRight, targetBottom)],
            3);

        Assert.Equal(
            new WindowSnapBounds(expectedLeft, expectedTop, expectedRight, expectedBottom),
            result);
    }

    [Theory]
    [InlineData(1, 11, 0, 21, 10, 10, 0, 21, 10)]
    [InlineData(2, 0, 0, 9, 10, 0, 0, 10, 10)]
    [InlineData(4, 0, 11, 10, 21, 0, 10, 10, 21)]
    [InlineData(8, 0, 0, 10, 9, 0, 0, 10, 10)]
    public void Resize_snaps_only_the_dragged_edge(
        int edge,
        int left,
        int top,
        int right,
        int bottom,
        int expectedLeft,
        int expectedTop,
        int expectedRight,
        int expectedBottom)
    {
        var result = WindowSnapGeometry.Resize(
            new WindowSnapBounds(left, top, right, bottom),
            [new WindowSnapBounds(10, 10, 20, 20)],
            2,
            (WindowSnapEdges)edge);

        Assert.Equal(
            new WindowSnapBounds(expectedLeft, expectedTop, expectedRight, expectedBottom),
            result);
    }

    [Fact]
    public void Snap_rejects_negative_maximum_distance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WindowSnapGeometry.Snap(new WindowSnapBounds(0, 0, 10, 10), [], -1));
    }
}
