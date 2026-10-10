using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace Azunote.Tests;

public sealed class NativeCrashReporterTests
{
    [Fact]
    public void Minidump_exception_information_matches_the_packed_windows_layout()
    {
        var type = typeof(NativeCrashReporter).GetNestedType(
            "MiniDumpExceptionInformation", BindingFlags.NonPublic);
        Assert.NotNull(type);

        // MINIDUMP_EXCEPTION_INFORMATION is declared with 4-byte packing.
        Assert.Equal(4, (int)Marshal.OffsetOf(type, "ExceptionPointers"));
        Assert.Equal(4 + IntPtr.Size, (int)Marshal.OffsetOf(type, "ClientPointers"));
        Assert.Equal(8 + IntPtr.Size, Marshal.SizeOf(type));
    }
}
