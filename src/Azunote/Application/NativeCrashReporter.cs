using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Azunote;

/// <summary>
/// Installs a last-resort native exception filter for failures that never
/// reach the managed UnhandledException events. The dump path is allocated
/// before a crash so the handler does not need to perform managed work.
/// </summary>
internal static unsafe partial class NativeCrashReporter
{
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint CreateAlways = 2;
    private const uint FileAttributeNormal = 0x00000080;
    private const nint InvalidHandleValue = -1;
    private const int ExceptionExecuteHandler = 1;

    private static readonly MiniDumpType DumpType =
        MiniDumpType.WithHandleData
        | MiniDumpType.WithUnloadedModules
        | MiniDumpType.WithIndirectlyReferencedMemory
        | MiniDumpType.WithPrivateReadWriteMemory
        | MiniDumpType.WithFullMemoryInfo
        | MiniDumpType.WithThreadInfo;

    private static nint _dumpPathPointer;
    private static nint _processHandle;
    private static uint _processId;
    private static string? _dumpPath;

    public static string? Install()
    {
        try
        {
            if (_dumpPathPointer == nint.Zero)
            {
                var directory = Path.Combine(
                    Path.GetTempPath(),
                    "Azunote",
                    "crashes");
                Directory.CreateDirectory(directory);
                _dumpPath = Path.Combine(
                    directory,
                    $"azunote-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}.dmp");
                _dumpPathPointer = Marshal.StringToCoTaskMemUni(_dumpPath);
                _processHandle = GetCurrentProcess();
                _processId = GetCurrentProcessId();
            }

            SetUnhandledExceptionFilter(&OnUnhandledException);
            return _dumpPath;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Azunote native crash reporter setup failed: {exception}");
            return null;
        }
    }

    public static string? FindLatestPreviousDump()
    {
        try
        {
            if (_dumpPath is null)
            {
                return null;
            }

            var directory = Path.GetDirectoryName(_dumpPath);
            if (directory is null || !Directory.Exists(directory))
            {
                return null;
            }

            return Directory.EnumerateFiles(directory, "*.dmp")
                .Where(path => !string.Equals(path, _dumpPath, StringComparison.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Azunote native crash dump discovery failed: {exception}");
            return null;
        }
    }

    /// <summary>
    /// Captures the current process when a managed unhandled-exception event
    /// fires. Some WinUI failures continue into a native crash after that
    /// event, so this snapshot is intentionally taken before the UI handler
    /// returns.
    /// </summary>
    public static string? WriteSnapshotDump()
    {
        try
        {
            return _dumpPath is not null && WriteDump(nint.Zero)
                ? _dumpPath
                : null;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Azunote managed exception dump failed: {exception}");
            return null;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int OnUnhandledException(nint exceptionPointers)
    {
        WriteDump(exceptionPointers);
        return ExceptionExecuteHandler;
    }

    private static bool WriteDump(nint exceptionPointers)
    {
        var pathPointer = _dumpPathPointer;
        var processHandle = _processHandle;
        if (pathPointer == nint.Zero || processHandle == nint.Zero)
        {
            return false;
        }

        var dumpFile = CreateFileW(
            pathPointer,
            GenericWrite,
            FileShareRead,
            nint.Zero,
            CreateAlways,
            FileAttributeNormal,
            nint.Zero);
        if (dumpFile == InvalidHandleValue)
        {
            return false;
        }

        var succeeded = false;
        try
        {
            var exceptionInformation = new MiniDumpExceptionInformation
            {
                ThreadId = GetCurrentThreadId(),
                ExceptionPointers = exceptionPointers,
                ClientPointers = 0
            };
            var exceptionInformationPointer = (nint)(&exceptionInformation);
            succeeded = MiniDumpWriteDump(
                processHandle,
                _processId,
                dumpFile,
                DumpType,
                exceptionInformationPointer,
                nint.Zero,
                nint.Zero) != 0;
        }
        finally
        {
            CloseHandle(dumpFile);
        }

        return succeeded;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentProcessId();

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint SetUnhandledExceptionFilter(
        delegate* unmanaged[Stdcall]<nint, int> exceptionFilter);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true)]
    private static partial nint CreateFileW(
        nint fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int CloseHandle(nint handle);

    [LibraryImport("dbghelp.dll", SetLastError = true)]
    private static partial int MiniDumpWriteDump(
        nint process,
        uint processId,
        nint file,
        MiniDumpType dumpType,
        nint exceptionParam,
        nint userStreamParam,
        nint callbackParam);

    [Flags]
    private enum MiniDumpType : uint
    {
        WithHandleData = 0x00000004,
        WithUnloadedModules = 0x00000020,
        WithIndirectlyReferencedMemory = 0x00000040,
        WithPrivateReadWriteMemory = 0x00000200,
        WithFullMemoryInfo = 0x00000800,
        WithThreadInfo = 0x00001000
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MiniDumpExceptionInformation
    {
        public uint ThreadId;
        public nint ExceptionPointers;
        public int ClientPointers;
    }
}
