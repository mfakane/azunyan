using System.Runtime.InteropServices;

namespace Azunote;

/// <summary>
/// Stops the beep that answers a shortcut holding Alt.
/// </summary>
/// <remarks>
/// A shortcut such as Alt+Shift+F runs its tool and then sounds, because the
/// key travels two paths. XAML invokes the accelerator, and separately the
/// system turns the keystroke into WM_SYSCHAR, asks the window which menu
/// mnemonic it names, and beeps when the answer is none. A XAML menu bar is
/// not a window menu, so the answer is always none and the beep always
/// follows. Answering MNC_CLOSE says there is no menu to look in rather than
/// no match within one, which is both true here and silent.
/// </remarks>
internal static unsafe partial class WindowMenuBeepSuppressor
{
    private const uint MenuCharMessage = 0x0120;

    /// <summary>MNC_CLOSE, which the reply carries in its high word.</summary>
    private const nint CloseMenu = 1 << 16;

    private const nuint SubclassId = 1;

    /// <summary>
    /// Keyboard input goes to the island window XAML draws into rather than to
    /// the window that owns it, and that is where the question arrives, so
    /// every window below this one is covered as well. Install once the
    /// content has loaded: the island is not there before that.
    /// </summary>
    internal static void Install(nint windowHandle)
    {
        SetWindowSubclass(windowHandle, &OnMessage, SubclassId, 0);
        _ = EnumChildWindows(windowHandle, &OnChildWindow, 0);
    }

    [UnmanagedCallersOnly]
    private static int OnChildWindow(nint windowHandle, nint data)
    {
        SetWindowSubclass(windowHandle, &OnMessage, SubclassId, 0);
        return 1;
    }

    [UnmanagedCallersOnly]
    private static nint OnMessage(
        nint windowHandle,
        uint message,
        nint wParam,
        nint lParam,
        nuint id,
        nuint data) =>
        message == MenuCharMessage
            ? CloseMenu
            : DefSubclassProc(windowHandle, message, wParam, lParam);

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

    [LibraryImport("user32.dll")]
    private static partial int EnumChildWindows(
        nint windowHandle,
        delegate* unmanaged<nint, nint, int> callback,
        nint data);
}
