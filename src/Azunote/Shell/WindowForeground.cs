using Windows.Win32;
using Windows.Win32.Foundation;

namespace Azunote;

/// <summary>
/// Brings a window to the front on an explicit request, such as opening a
/// document in the already running editor. Windows refuses a foreground change
/// from a process that does not own the foreground, so the plain call is only
/// the first attempt: sharing the input queue of the window that does own it
/// makes the switch happen the way a user-driven one would.
/// </summary>
internal static class WindowForeground
{
    public static void BringToFront(IntPtr windowHandle)
    {
        var target = (HWND)windowHandle;
        if (target.IsNull || PInvoke.SetForegroundWindow(target))
        {
            return;
        }

        var foreground = PInvoke.GetForegroundWindow();
        if (foreground.IsNull || foreground == target)
        {
            return;
        }

        var foregroundThread = PInvoke.GetWindowThreadProcessId(foreground);
        var currentThread = PInvoke.GetCurrentThreadId();
        if (foregroundThread == 0 || foregroundThread == currentThread)
        {
            return;
        }

        if (!PInvoke.AttachThreadInput(currentThread, foregroundThread, true))
        {
            return;
        }

        try
        {
            PInvoke.SetForegroundWindow(target);
            PInvoke.BringWindowToTop(target);
        }
        finally
        {
            PInvoke.AttachThreadInput(currentThread, foregroundThread, false);
        }
    }
}
