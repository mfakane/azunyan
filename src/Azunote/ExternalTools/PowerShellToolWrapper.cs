using System.Reflection;
using System.Text;

namespace Azunote;

/// <summary>
/// The script every `pwsh` tool runs, and the command line that hands it to
/// PowerShell.
///
/// The script is `PowerShellToolWrapper.ps1`, embedded rather than shipped as a
/// file: PowerShell is given it as an encoded command, so no execution policy
/// can keep a tool from running, while the script itself stays a PowerShell
/// file that can be read, linted and run on its own.
///
/// One script serves both ways a process is started. A process started for a
/// request reads what to run from its environment; a waiting process, started
/// before its request is known, reads it from the handshake pipe instead. The
/// command line is therefore the same for both.
/// </summary>
internal static class PowerShellToolWrapper
{
    private const string ResourceName = "Azunote.ExternalTools.PowerShellToolWrapper.ps1";

    private static string? _script;
    private static string? _arguments;

    /// <summary>The wrapper script, as PowerShell source.</summary>
    internal static string Script => _script ??= Load();

    /// <summary>
    /// The arguments PowerShell is started with. The encoding is paid once:
    /// the script does not change between runs.
    /// </summary>
    internal static string Arguments => _arguments ??=
        "-NoLogo -NoProfile -NonInteractive -EncodedCommand "
        + Convert.ToBase64String(Encoding.Unicode.GetBytes(Script));

    private static string Load()
    {
        using var stream = typeof(PowerShellToolWrapper).Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The external tool wrapper script is missing: {ResourceName}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
