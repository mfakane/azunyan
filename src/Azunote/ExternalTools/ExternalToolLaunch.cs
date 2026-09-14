using System.Diagnostics;
using System.Text;

namespace Azunote;

internal enum ExternalToolLaunchKind
{
    Direct,
    CommandShell,
    CommandShellCommand,
    PowerShell,
    PowerShellCommand
}

internal sealed record ExternalToolLaunchPlan(
    string RequestedCommand,
    string ResolvedPath,
    string LauncherPath,
    ExternalToolLaunchKind Kind)
{
    private const string ShellCommandEnvironmentVariable =
        "AZUNOTE_EXTERNAL_TOOL_COMMAND";
    private static readonly UnicodeEncoding CommandShellOutputEncoding =
        new(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: false);

    public void AddArguments(
        ProcessStartInfo startInfo,
        IReadOnlyList<string> arguments)
    {
        switch (Kind)
        {
            case ExternalToolLaunchKind.Direct:
                AddAll(startInfo, arguments);
                break;
            case ExternalToolLaunchKind.CommandShell:
                startInfo.Arguments = "/d /s /c "
                    + BuildCommandShellCommand(ResolvedPath, arguments);
                break;
            case ExternalToolLaunchKind.CommandShellCommand:
                startInfo.Environment[ShellCommandEnvironmentVariable] = ResolvedPath;
                startInfo.StandardOutputEncoding = CommandShellOutputEncoding;
                startInfo.StandardErrorEncoding = CommandShellOutputEncoding;
                startInfo.Arguments = BuildRawCommandShellCommand();
                break;
            case ExternalToolLaunchKind.PowerShell:
                startInfo.Arguments = "-NoLogo -NoProfile -NonInteractive -File " + QuoteCommandShellArgument(ResolvedPath);
                AddAll(startInfo, arguments);
                break;
            case ExternalToolLaunchKind.PowerShellCommand:
                startInfo.Environment[ShellCommandEnvironmentVariable] = ResolvedPath;
                startInfo.Arguments = "-NoLogo -NoProfile -NonInteractive -Command "
                    + BuildPowerShellCommand();
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported external tool launch kind: {Kind}.");
        }
    }

    private static void AddAll(
        ProcessStartInfo startInfo,
        IReadOnlyList<string> arguments)
    {
        startInfo.Arguments += (startInfo.Arguments.Length > 0 ? " " : "") + string.Join(" ", arguments.Select(x => x.Contains(' ') ? QuoteCommandShellArgument(x) : x));
    }

    private static string BuildCommandShellCommand(
        string scriptPath,
        IReadOnlyList<string> arguments)
    {
        var commandParts = new List<string>(arguments.Count + 2)
        {
            "call",
            QuoteCommandShellArgument(scriptPath)
        };
        commandParts.AddRange(arguments.Select(QuoteCommandShellArgument));
        return "chcp 65001 >nul & " + string.Join(' ', commandParts);
    }

    private static string BuildRawCommandShellCommand() =>
        "/u /d /s /c chcp 65001 >nul & call %"
        + ShellCommandEnvironmentVariable
        + "%";

    private static string BuildPowerShellCommand() =>
        "$OutputEncoding = [System.Text.UTF8Encoding]::new($false); "
        + "[Console]::OutputEncoding = $OutputEncoding; "
        + "Invoke-Expression $env:" + ShellCommandEnvironmentVariable;

    /// <summary>
    /// Arguments for a process started before its request is known. The
    /// process sets up the same encoding as a cold launch, parks on the
    /// handshake pipe, and then applies the working directory, environment
    /// overrides and script it receives. Standard input is never touched, so
    /// it stays available to the script itself. The syntax is limited to what
    /// both PowerShell 7 and Windows PowerShell 5.1 accept.
    /// </summary>
    internal static string BuildPowerShellWarmArguments() =>
        "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + EncodeCommand(WarmBootstrapScript);

    private const string WarmBootstrapScript = """
        $OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        [Console]::OutputEncoding = $OutputEncoding
        $azunoteScript = $null
        try
        {
            $ErrorActionPreference = 'Stop'
            $azunotePipeName = $env:AZUNOTE_EXTERNAL_TOOL_PIPE
            $azunoteToken = $env:AZUNOTE_EXTERNAL_TOOL_TOKEN
            [Environment]::SetEnvironmentVariable('AZUNOTE_EXTERNAL_TOOL_PIPE', $null)
            [Environment]::SetEnvironmentVariable('AZUNOTE_EXTERNAL_TOOL_TOKEN', $null)
            $azunotePipe = New-Object -TypeName System.IO.Pipes.NamedPipeClientStream -ArgumentList '.', $azunotePipeName, ([System.IO.Pipes.PipeDirection]::InOut)
            $azunotePipe.Connect(60000)
            $azunoteWriter = New-Object -TypeName System.IO.StreamWriter -ArgumentList $azunotePipe, ([System.Text.UTF8Encoding]::new($false))
            $azunoteWriter.AutoFlush = $true
            $azunoteWriter.WriteLine($azunoteToken)
            $azunoteReader = New-Object -TypeName System.IO.StreamReader -ArgumentList $azunotePipe, ([System.Text.UTF8Encoding]::new($false))
            $azunoteRequest = $azunoteReader.ReadLine() | ConvertFrom-Json
            $azunotePipe.Dispose()
            Set-Location -LiteralPath $azunoteRequest.cwd
            [Environment]::CurrentDirectory = $azunoteRequest.cwd
            foreach ($azunoteEntry in $azunoteRequest.env.PSObject.Properties)
            {
                [Environment]::SetEnvironmentVariable($azunoteEntry.Name, $azunoteEntry.Value)
            }

            $azunoteScript = [string]$azunoteRequest.script
        }
        catch
        {
            exit 199
        }

        $ErrorActionPreference = 'Continue'
        Remove-Variable -Name azunotePipeName, azunoteToken, azunotePipe, azunoteWriter, azunoteReader, azunoteRequest, azunoteEntry -ErrorAction SilentlyContinue
        Invoke-Expression $azunoteScript
        """;

    private static string EncodeCommand(string command) =>
        Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

    private static string QuoteCommandShellArgument(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}

internal static class ExternalToolLaunchResolver
{
    public static ExternalToolLaunchPlan? Resolve(
        string command,
        string? definitionDirectory) =>
        Resolve(command, ExternalToolCommandMode.Executable, definitionDirectory);

    public static ExternalToolLaunchPlan? Resolve(
        string command,
        ExternalToolCommandMode commandMode,
        string? definitionDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        using var measurement = ShellPerformance.Measure("command.search");

        if (commandMode == ExternalToolCommandMode.Cmd)
        {
            var commandShell = ResolveCommandShell();
            return commandShell is null
                ? null
                : new ExternalToolLaunchPlan(
                    command,
                    command,
                    commandShell,
                    ExternalToolLaunchKind.CommandShellCommand);
        }

        if (commandMode == ExternalToolCommandMode.Pwsh)
        {
            var powerShell = ResolvePowerShell();
            return powerShell is null
                ? null
                : new ExternalToolLaunchPlan(
                    command,
                    command,
                    powerShell,
                    ExternalToolLaunchKind.PowerShellCommand);
        }

        var resolvedPath = ResolveExecutable(command, definitionDirectory);
        if (resolvedPath is null)
        {
            return null;
        }

        var extension = Path.GetExtension(resolvedPath);
        if (OperatingSystem.IsWindows())
        {
            if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
            {
                var commandShell = ResolveCommandShell();
                return commandShell is null
                    ? null
                    : new ExternalToolLaunchPlan(
                        command,
                        resolvedPath,
                        commandShell,
                        ExternalToolLaunchKind.CommandShell);
            }

            if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                var powerShell = ResolvePowerShell();
                return powerShell is null
                    ? null
                    : new ExternalToolLaunchPlan(
                        command,
                        resolvedPath,
                        powerShell,
                        ExternalToolLaunchKind.PowerShell);
            }
        }

        return new ExternalToolLaunchPlan(
            command,
            resolvedPath,
            resolvedPath,
            ExternalToolLaunchKind.Direct);
    }

    private static string? ResolveExecutable(
        string command,
        string? definitionDirectory)
    {
        command = command.Trim();
        var hasPath = Path.IsPathRooted(command)
            || command.Contains(Path.DirectorySeparatorChar)
            || command.Contains(Path.AltDirectorySeparatorChar);
        if (hasPath)
        {
            var path = Path.IsPathRooted(command) || string.IsNullOrWhiteSpace(definitionDirectory)
                ? command
                : Path.Combine(definitionDirectory, command);
            return ResolvePath(path, allowExtensionless: true);
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var rawDirectory in pathVariable.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = rawDirectory.Trim().Trim('"');
            if (directory.Length == 0)
            {
                continue;
            }

            var path = Path.Combine(directory, command);
            var resolved = ResolvePath(path, allowExtensionless: false);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static string? ResolvePath(string path, bool allowExtensionless)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath)
            && (allowExtensionless || Path.GetExtension(fullPath).Length > 0))
        {
            return fullPath;
        }

        if (!OperatingSystem.IsWindows() || Path.GetExtension(fullPath).Length > 0)
        {
            return null;
        }

        foreach (var extension in GetWindowsExecutableExtensions())
        {
            var candidate = fullPath + extension;
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetWindowsExecutableExtensions()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        var pathExtensions = Environment.GetEnvironmentVariable("PATHEXT")
            ?? ".COM;.EXE;.BAT;.CMD";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in pathExtensions.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (seen.Add(extension))
            {
                yield return extension;
            }
        }

        if (seen.Add(".PS1"))
        {
            yield return ".PS1";
        }
    }

    private static string? ResolveCommandShell()
    {
        var systemCommandShell = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        return File.Exists(systemCommandShell)
            ? systemCommandShell
            : ResolveExecutable("cmd.exe", null);
    }

    internal static string? ResolvePowerShell() =>
        ResolveExecutable("pwsh.exe", null)
        ?? ResolveExecutable("powershell.exe", null);
}
