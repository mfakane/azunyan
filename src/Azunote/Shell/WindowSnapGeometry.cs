namespace Azunote;

internal readonly record struct WindowSnapBounds(int Left, int Top, int Right, int Bottom);
internal readonly record struct WindowSnapPoint(int X, int Y);

[Flags]
internal enum WindowSnapEdges
{
    None = 0,
    Left = 1,
    Right = 2,
    Top = 4,
    Bottom = 8
}

internal sealed class WindowSnapReleaseTracker(int releaseDistance, int rearmDistance)
{
    private WindowSnapPoint? _dragOrigin;
    private WindowSnapPoint? _origin;
    private WindowSnapBounds _boundsAtOrigin;

    internal bool TryRelease(WindowSnapPoint position, out WindowSnapBounds bounds)
    {
        if (_origin is not { } origin ||
            !WindowSnapGeometry.HasMovedAtLeast(origin, position, releaseDistance))
        {
            bounds = default;
            return false;
        }

        var horizontalDistance = position.X - origin.X;
        var verticalDistance = position.Y - origin.Y;
        bounds = new WindowSnapBounds(
            _boundsAtOrigin.Left + horizontalDistance,
            _boundsAtOrigin.Top + verticalDistance,
            _boundsAtOrigin.Right + horizontalDistance,
            _boundsAtOrigin.Bottom + verticalDistance);

        if (WindowSnapGeometry.HasMovedAtLeast(origin, position, rearmDistance))
        {
            _origin = null;
        }

        return true;
    }

    internal void ObserveSnap(WindowSnapPoint position, WindowSnapBounds adjustedBounds)
    {
        if (_origin is null)
        {
            _origin = _dragOrigin ?? position;
            var horizontalDistance = position.X - _origin.Value.X;
            var verticalDistance = position.Y - _origin.Value.Y;
            _boundsAtOrigin = new WindowSnapBounds(
                adjustedBounds.Left - horizontalDistance,
                adjustedBounds.Top - verticalDistance,
                adjustedBounds.Right - horizontalDistance,
                adjustedBounds.Bottom - verticalDistance);
        }

        _dragOrigin = null;
    }

    internal void BeginDrag(WindowSnapPoint position)
    {
        _dragOrigin = position;
        _origin = null;
    }

    internal void Reset()
    {
        _dragOrigin = null;
        _origin = null;
    }
}

internal static class WindowSnapGeometry
{
    internal static bool IsFastMovement(
        WindowSnapPoint previous,
        WindowSnapPoint current,
        TimeSpan elapsed,
        double maximumSpeed)
    {
        if (elapsed <= TimeSpan.Zero)
        {
            return false;
        }

        var horizontalDistance = (long)current.X - previous.X;
        var verticalDistance = (long)current.Y - previous.Y;
        var maximumDistance = maximumSpeed * elapsed.TotalSeconds;
        return (double)horizontalDistance * horizontalDistance +
            (double)verticalDistance * verticalDistance > maximumDistance * maximumDistance;
    }

    internal static bool HasMovedAtLeast(
        WindowSnapPoint origin,
        WindowSnapPoint current,
        int distance)
    {
        var horizontalDistance = (long)current.X - origin.X;
        var verticalDistance = (long)current.Y - origin.Y;
        return (double)horizontalDistance * horizontalDistance +
            (double)verticalDistance * verticalDistance >= (double)distance * distance;
    }

    internal static WindowSnapBounds Snap(WindowSnapBounds moving, IEnumerable<WindowSnapBounds> targets, int maximumDistance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDistance);

        var horizontalAdjustment = 0;
        var verticalAdjustment = 0;
        var closestHorizontalDistance = maximumDistance + 1L;
        var closestVerticalDistance = maximumDistance + 1L;

        foreach (var target in targets)
        {
            if (moving.Top < target.Bottom && target.Top < moving.Bottom)
            {
                Consider(moving.Right, target.Left, ref horizontalAdjustment, ref closestHorizontalDistance);
                Consider(moving.Left, target.Right, ref horizontalAdjustment, ref closestHorizontalDistance);
            }

            if (RangesAreNear(moving.Top, moving.Bottom, target.Top, target.Bottom, maximumDistance))
            {
                Consider(moving.Left, target.Left, ref horizontalAdjustment, ref closestHorizontalDistance);
                Consider(moving.Right, target.Right, ref horizontalAdjustment, ref closestHorizontalDistance);
            }

            if (moving.Left < target.Right && target.Left < moving.Right)
            {
                Consider(moving.Bottom, target.Top, ref verticalAdjustment, ref closestVerticalDistance);
                Consider(moving.Top, target.Bottom, ref verticalAdjustment, ref closestVerticalDistance);
            }

            if (RangesAreNear(moving.Left, moving.Right, target.Left, target.Right, maximumDistance))
            {
                Consider(moving.Top, target.Top, ref verticalAdjustment, ref closestVerticalDistance);
                Consider(moving.Bottom, target.Bottom, ref verticalAdjustment, ref closestVerticalDistance);
            }
        }

        return new WindowSnapBounds(
            moving.Left + horizontalAdjustment,
            moving.Top + verticalAdjustment,
            moving.Right + horizontalAdjustment,
            moving.Bottom + verticalAdjustment);
    }

    internal static WindowSnapBounds Resize(
        WindowSnapBounds resizing,
        IEnumerable<WindowSnapBounds> targets,
        int maximumDistance,
        WindowSnapEdges edges)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDistance);

        var leftAdjustment = 0;
        var rightAdjustment = 0;
        var topAdjustment = 0;
        var bottomAdjustment = 0;
        var closestLeftDistance = maximumDistance + 1L;
        var closestRightDistance = maximumDistance + 1L;
        var closestTopDistance = maximumDistance + 1L;
        var closestBottomDistance = maximumDistance + 1L;

        foreach (var target in targets)
        {
            if (RangesAreNear(resizing.Top, resizing.Bottom, target.Top, target.Bottom, maximumDistance))
            {
                if ((edges & WindowSnapEdges.Left) != 0)
                {
                    Consider(resizing.Left, target.Left, ref leftAdjustment, ref closestLeftDistance);
                    Consider(resizing.Left, target.Right, ref leftAdjustment, ref closestLeftDistance);
                }

                if ((edges & WindowSnapEdges.Right) != 0)
                {
                    Consider(resizing.Right, target.Left, ref rightAdjustment, ref closestRightDistance);
                    Consider(resizing.Right, target.Right, ref rightAdjustment, ref closestRightDistance);
                }
            }

            if (RangesAreNear(resizing.Left, resizing.Right, target.Left, target.Right, maximumDistance))
            {
                if ((edges & WindowSnapEdges.Top) != 0)
                {
                    Consider(resizing.Top, target.Top, ref topAdjustment, ref closestTopDistance);
                    Consider(resizing.Top, target.Bottom, ref topAdjustment, ref closestTopDistance);
                }

                if ((edges & WindowSnapEdges.Bottom) != 0)
                {
                    Consider(resizing.Bottom, target.Top, ref bottomAdjustment, ref closestBottomDistance);
                    Consider(resizing.Bottom, target.Bottom, ref bottomAdjustment, ref closestBottomDistance);
                }
            }
        }

        return new WindowSnapBounds(
            resizing.Left + leftAdjustment,
            resizing.Top + topAdjustment,
            resizing.Right + rightAdjustment,
            resizing.Bottom + bottomAdjustment);
    }

    private static bool RangesAreNear(int firstStart, int firstEnd, int secondStart, int secondEnd, int distance) =>
        (long)firstStart <= (long)secondEnd + distance &&
        (long)secondStart <= (long)firstEnd + distance;

    private static void Consider(int movingEdge, int targetEdge, ref int adjustment, ref long closestDistance)
    {
        var distance = (long)targetEdge - movingEdge;
        var absoluteDistance = Math.Abs(distance);
        if (absoluteDistance < closestDistance)
        {
            closestDistance = absoluteDistance;
            adjustment = (int)distance;
        }
    }
}
