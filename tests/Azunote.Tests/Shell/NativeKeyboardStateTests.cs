using System.Runtime.InteropServices;
using Azunyan.WinUI;
using Windows.System;
using Xunit;

namespace Azunote.Tests.Shell;

public sealed class NativeKeyboardStateTests
{
    [Theory]
    [InlineData(VirtualKey.Kanji)]
    [InlineData(VirtualKey.Menu)]
    public void Released_key_does_not_block_input_sync_when_thread_state_still_reports_down(VirtualKey key)
    {
        var original = new byte[256];
        Assert.True(GetKeyboardState(original));
        try
        {
            // Reproduce the thread state left behind by a consumed KeyUp,
            // without changing the physical keyboard or starting an IME.
            var stale = (byte[])original.Clone();
            stale[(int)key] = 0x80;
            Assert.True(SetKeyboardState(stale));
            Assert.NotEqual(0, GetKeyState((int)key) & 0x8000);
            var keys = new HashSet<VirtualKey> { key };

            NativeKeyboardState.ForgetReleasedKeys(keys);

            Assert.Empty(keys);
        }
        finally
        {
            Assert.True(SetKeyboardState(original));
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState(byte[] state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKeyboardState(byte[] state);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int key);
}
