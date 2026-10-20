using System.Runtime.InteropServices;

namespace Azunote;

/// <summary>
/// Runs a callback when Windows asks whether the session may end, before it
/// shuts down, restarts, or signs out. The process can be stopped after that
/// without its windows closing, so this is the last point to save state.
/// </summary>
internal static unsafe partial class SessionEndWatcher
{
    private const uint QueryEndSessionMessage = 0x0011;
    private const nuint SubclassId = 3;

    private static Action? _sessionEnding;

    internal static void Install(nint windowHandle, Action sessionEnding)
    {
        _sessionEnding = sessionEnding ?? throw new ArgumentNullException(nameof(sessionEnding));
        SetWindowSubclass(windowHandle, &OnMessage, SubclassId, 0);
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
        if (message == QueryEndSessionMessage)
        {
            try
            {
                _sessionEnding?.Invoke();
            }
            catch (Exception exception)
            {
                // Native callbacks must not allow managed exceptions to cross the ABI boundary.
                ErrorReporter.LogException("Session end", exception);
            }
        }

        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    [LibraryImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(
        nint windowHandle,
        delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> callback,
        nuint id,
        nuint data);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(
        nint windowHandle,
        uint message,
        nint wParam,
        nint lParam);
}
