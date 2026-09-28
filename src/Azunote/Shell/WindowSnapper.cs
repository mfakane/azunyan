using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Azunote;

internal static unsafe partial class WindowSnapper
{
    private const uint SizingMessage = 0x0214;
    private const uint MovingMessage = 0x0216;
    private const uint EnterSizeMoveMessage = 0x0231;
    private const uint NonClientDestroyMessage = 0x0082;
    private const uint ExtendedFrameBoundsAttribute = 9;
    private const nuint SubclassId = 2;
    private const double MaximumSnapSpeed = 1000;
    private const int SnapReleaseDistance = 8;
    private const int SnapRearmDistance = 16;
    private static readonly Dictionary<nint, WindowState> Windows = [];

    internal static void Install(MainWindow window)
    {
        var handle = window.WindowHandle;
        lock (Windows)
        {
            Windows[handle] = new WindowState(window);
        }

        if (!SetWindowSubclass(handle, &OnMessage, SubclassId, 0))
        {
            lock (Windows)
            {
                Windows.Remove(handle);
            }
        }
    }

    internal static void Remove(MainWindow window)
    {
        var handle = window.WindowHandle;
        lock (Windows)
        {
            Windows.Remove(handle);
        }

        _ = RemoveWindowSubclass(handle, &OnMessage, SubclassId);
    }

    internal static void SetEnabled(MainWindow window, bool enabled)
    {
        lock (Windows)
        {
            if (Windows.TryGetValue(window.WindowHandle, out var state))
            {
                state.SetEnabled(enabled);
            }
        }
    }

    internal static bool TryGetBounds(MainWindow window, out WindowSnapBounds bounds)
    {
        var handle = window.WindowHandle;
        if (IsWindowVisible(handle) && !IsIconic(handle) && !IsZoomed(handle) &&
            TryGetVisibleBounds(handle, out var nativeBounds))
        {
            bounds = nativeBounds.ToSnapBounds();
            return true;
        }

        bounds = default;
        return false;
    }

    [UnmanagedCallersOnly]
    private static nint OnMessage(
        nint windowHandle,
        uint message,
        nint wParam,
        nint lParam,
        nuint id,
        nuint data)
    {
        try
        {
            if ((message == MovingMessage || message == SizingMessage) && lParam != 0)
            {
                WindowState? state;
                lock (Windows)
                {
                    Windows.TryGetValue(windowHandle, out state);
                }

                if (state is not null)
                {
                    if (!state.IsEnabled)
                    {
                        return DefSubclassProc(windowHandle, message, wParam, lParam);
                    }

                    ref var nativeBounds = ref *(NativeRect*)lParam;
                    var outerBounds = nativeBounds.ToSnapBounds();
                    var cursorPosition = GetCursorPosition(outerBounds);
                    if (message == MovingMessage &&
                        state.ShouldSkipSnap(cursorPosition, out var releasedBounds))
                    {
                        if (releasedBounds is { } released)
                        {
                            nativeBounds = NativeRect.FromSnapBounds(released);
                        }

                        _ = DefSubclassProc(windowHandle, message, wParam, lParam);
                        return 1;
                    }

                    var visibleBounds = ToVisibleBounds(windowHandle, outerBounds);
                    var snappedBounds = state.Window.SnapWindow(
                        visibleBounds,
                        message == SizingMessage ? GetSizingEdges(wParam) : WindowSnapEdges.None);
                    var adjustedOuterBounds = ApplyVisibleAdjustment(
                        outerBounds,
                        visibleBounds,
                        snappedBounds);
                    if (message == MovingMessage)
                    {
                        state.ObserveSnap(outerBounds, adjustedOuterBounds, cursorPosition);
                    }

                    nativeBounds = NativeRect.FromSnapBounds(adjustedOuterBounds);
                    _ = DefSubclassProc(windowHandle, message, wParam, lParam);
                    return 1;
                }
            }
            else if (message == EnterSizeMoveMessage)
            {
                lock (Windows)
                {
                    if (Windows.TryGetValue(windowHandle, out var state))
                    {
                        state.BeginMovement(GetCursorPosition(
                            GetWindowRect(windowHandle, out var bounds)
                                ? bounds.ToSnapBounds()
                                : default));
                    }
                }
            }
            else if (message == NonClientDestroyMessage)
            {
                lock (Windows)
                {
                    Windows.Remove(windowHandle);
                }

                _ = RemoveWindowSubclass(windowHandle, &OnMessage, SubclassId);
            }
        }
        catch
        {
            // Native callbacks must not allow managed exceptions to cross the ABI boundary.
        }

        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    private static WindowSnapBounds ToVisibleBounds(nint windowHandle, WindowSnapBounds outerBounds)
    {
        if (!GetWindowRect(windowHandle, out var currentOuter) ||
            !TryGetVisibleBounds(windowHandle, out var currentVisible))
        {
            return outerBounds;
        }

        return new WindowSnapBounds(
            outerBounds.Left + currentVisible.Left - currentOuter.Left,
            outerBounds.Top + currentVisible.Top - currentOuter.Top,
            outerBounds.Right + currentVisible.Right - currentOuter.Right,
            outerBounds.Bottom + currentVisible.Bottom - currentOuter.Bottom);
    }

    private static WindowSnapBounds ApplyVisibleAdjustment(
        WindowSnapBounds outerBounds,
        WindowSnapBounds visibleBounds,
        WindowSnapBounds snappedBounds) => new(
            outerBounds.Left + snappedBounds.Left - visibleBounds.Left,
            outerBounds.Top + snappedBounds.Top - visibleBounds.Top,
            outerBounds.Right + snappedBounds.Right - visibleBounds.Right,
            outerBounds.Bottom + snappedBounds.Bottom - visibleBounds.Bottom);

    private static WindowSnapPoint GetCursorPosition(WindowSnapBounds fallback) =>
        GetCursorPos(out var position)
            ? new WindowSnapPoint(position.X, position.Y)
            : new WindowSnapPoint(fallback.Left, fallback.Top);

    private static bool TryGetVisibleBounds(nint windowHandle, out NativeRect bounds)
    {
        if (DwmGetWindowAttribute(
                windowHandle,
                ExtendedFrameBoundsAttribute,
                out bounds,
                (uint)sizeof(NativeRect)) >= 0)
        {
            return true;
        }

        return GetWindowRect(windowHandle, out bounds);
    }

    private static WindowSnapEdges GetSizingEdges(nint sizingEdge) => (int)sizingEdge switch
    {
        1 => WindowSnapEdges.Left,
        2 => WindowSnapEdges.Right,
        3 => WindowSnapEdges.Top,
        4 => WindowSnapEdges.Top | WindowSnapEdges.Left,
        5 => WindowSnapEdges.Top | WindowSnapEdges.Right,
        6 => WindowSnapEdges.Bottom,
        7 => WindowSnapEdges.Bottom | WindowSnapEdges.Left,
        8 => WindowSnapEdges.Bottom | WindowSnapEdges.Right,
        _ => WindowSnapEdges.None
    };

    private sealed class WindowState(MainWindow window)
    {
        private readonly WindowSnapReleaseTracker _releaseTracker =
            new(SnapReleaseDistance, SnapRearmDistance);
        private WindowSnapPoint? _previousPosition;
        private long _previousTimestamp;

        internal MainWindow Window { get; } = window;
        internal bool IsEnabled { get; private set; } = true;

        internal void SetEnabled(bool enabled)
        {
            IsEnabled = enabled;
            if (!enabled)
            {
                _previousPosition = null;
                _previousTimestamp = 0;
                _releaseTracker.Reset();
            }
        }

        internal bool ShouldSkipSnap(
            WindowSnapPoint position,
            out WindowSnapBounds? releasedBounds)
        {
            var timestamp = Stopwatch.GetTimestamp();
            var isTooFast = _previousPosition is { } previous && WindowSnapGeometry.IsFastMovement(
                previous,
                position,
                Stopwatch.GetElapsedTime(_previousTimestamp, timestamp),
                MaximumSnapSpeed);
            var movedPastSnap = _releaseTracker.TryRelease(position, out var released);
            releasedBounds = movedPastSnap ? released : null;
            _previousPosition = position;
            _previousTimestamp = timestamp;
            return isTooFast || movedPastSnap;
        }

        internal void ObserveSnap(
            WindowSnapBounds proposed,
            WindowSnapBounds adjusted,
            WindowSnapPoint cursorPosition)
        {
            if (proposed != adjusted)
            {
                _releaseTracker.ObserveSnap(cursorPosition, adjusted);
            }
        }

        internal void BeginMovement(WindowSnapPoint position)
        {
            _previousPosition = position;
            _releaseTracker.BeginDrag(position);
            _previousTimestamp = Stopwatch.GetTimestamp();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal readonly WindowSnapBounds ToSnapBounds() =>
            new(Left, Top, Right, Bottom);

        internal static NativeRect FromSnapBounds(WindowSnapBounds bounds) => new()
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Right = bounds.Right,
            Bottom = bounds.Bottom
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [LibraryImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(
        nint windowHandle,
        delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> callback,
        nuint id,
        nuint data);

    [LibraryImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveWindowSubclass(
        nint windowHandle,
        delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> callback,
        nuint id);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(
        nint windowHandle,
        uint message,
        nint wParam,
        nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint windowHandle, out NativeRect bounds);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint position);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint windowHandle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint windowHandle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsZoomed(nint windowHandle);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(
        nint windowHandle,
        uint attribute,
        out NativeRect value,
        uint valueSize);
}
