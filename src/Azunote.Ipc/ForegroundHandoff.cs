using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Azunote;

/// <summary>
/// Windows only lets the process that currently owns the foreground decide
/// who may take it next. A launch that forwards its command line is that
/// process — the shell started it for the user — while the running editor is
/// not, so the editor's own attempt to activate a window is refused and only
/// flashes its taskbar button. Handing the right over before the command is
/// forwarded is what lets the editor come to the front.
/// </summary>
public static partial class ForegroundHandoff
{
    /// <summary>
    /// Grants the process on the other end of <paramref name="pipe"/> the
    /// right to take the foreground. The target is read from the pipe itself,
    /// so no part of the command needs to carry a process id.
    /// </summary>
    public static void AllowFor(PipeStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            if (GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var processId))
            {
                AllowSetForegroundWindow(processId);
            }
        }
        catch (EntryPointNotFoundException)
        {
            // Activation stays where it is; this is a courtesy, not a contract.
        }
        catch (DllNotFoundException)
        {
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(
        SafePipeHandle pipe,
        out uint processId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);
}
