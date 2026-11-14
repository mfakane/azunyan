using System.Runtime.InteropServices;
using Windows.System;

namespace Azunyan.WinUI;

internal static partial class NativeKeyboardState
{
    internal static void ForgetReleasedKeys(HashSet<VirtualKey> keys) =>
        // The thread's keyboard state can retain Kanji (or Alt after Alt+Tab)
        // when the IME or another window consumes its release. Consult the
        // current state only to retire keys; command modifiers still use the
        // state associated with the input message being processed.
        keys.RemoveWhere(key => (GetAsyncKeyState((int)key) & 0x8000) == 0);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int virtualKey);
}
