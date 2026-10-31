using Microsoft.UI.Dispatching;
using Windows.Foundation;

namespace Azunyan.WinUI;

/// <summary>
/// Work the dispatcher calls back, queued or on a timer's tick, stops the
/// process on the spot when it throws, before Application.UnhandledException
/// hears of it. These run such work so that its exception reaches that event,
/// as one thrown by a XAML event handler or after an await does.
/// </summary>
public static class DispatcherQueueReporting
{
    public static bool TryEnqueueReporting(this DispatcherQueue dispatcherQueue, DispatcherQueueHandler callback)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        ArgumentNullException.ThrowIfNull(callback);
        return dispatcherQueue.TryEnqueue(() => Run(callback));
    }

    public static bool TryEnqueueReporting(
        this DispatcherQueue dispatcherQueue,
        DispatcherQueuePriority priority,
        DispatcherQueueHandler callback)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        ArgumentNullException.ThrowIfNull(callback);
        return dispatcherQueue.TryEnqueue(priority, () => Run(callback));
    }

    /// <summary>Handles every tick of the timer.</summary>
    public static void OnTick(this DispatcherQueueTimer timer, TypedEventHandler<DispatcherQueueTimer, object> handler)
    {
        ArgumentNullException.ThrowIfNull(timer);
        ArgumentNullException.ThrowIfNull(handler);
        timer.Tick += (sender, args) => Run(() => handler(sender, args));
    }

    private static void Run(DispatcherQueueHandler callback)
    {
        try
        {
            callback();
        }
        catch (Exception exception)
        {
            WinRT.ExceptionHelpers.ReportUnhandledError(exception);
        }
    }
}
