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
    private const string ShellStandardInputEnvironmentVariable =
        "AZUNOTE_EXTERNAL_TOOL_STDIN";
    private static readonly UnicodeEncoding CommandShellOutputEncoding =
        new(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: false);

    public void AddArguments(
        ProcessStartInfo startInfo,
        IReadOnlyList<string> arguments,
        bool standardInputConfigured = false)
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
                startInfo.Environment[ShellStandardInputEnvironmentVariable] =
                    standardInputConfigured ? "1" : "0";
                startInfo.Arguments = "-NoLogo -NoProfile -NonInteractive -EncodedCommand "
                    + EncodeCommand(BuildPowerShellCommand());
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

    /// <summary>
    /// The script a `pwsh` tool is launched with. The command line itself
    /// carries no part of the configured command: it travels in an environment
    /// variable, and whether standard input is configured travels in another,
    /// so that quoting never has to be reasoned about.
    /// </summary>
    private static string BuildPowerShellCommand() =>
        EncodingPrologue
        + Environment.NewLine
        + "$azunoteScript = [string]$env:" + ShellCommandEnvironmentVariable
        + Environment.NewLine
        + "$azunoteHasInput = $env:" + ShellStandardInputEnvironmentVariable + " -eq '1'"
        + Environment.NewLine
        + RunScript;

    /// <summary>
    /// Arguments for a process started before its request is known. The
    /// process sets up the same encoding as a cold launch, parks on the
    /// handshake pipe, and then applies the working directory, environment
    /// overrides and script it receives, and then runs it exactly the way a
    /// cold launch does. The syntax is limited to what both PowerShell 7 and
    /// Windows PowerShell 5.1 accept.
    /// </summary>
    internal static string BuildPowerShellWarmArguments() =>
        "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + EncodeCommand(WarmBootstrapScript);

    private static string WarmBootstrapScript =>
        EncodingPrologue
        + Environment.NewLine
        + WarmHandshakeScript
        + Environment.NewLine
        + RunScript;

    private const string WarmHandshakeScript = """
        $azunoteScript = $null
        $azunoteHasInput = $false
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
            $azunoteHasInput = [bool]$azunoteRequest.stdin
        }
        catch
        {
            exit 199
        }

        $ErrorActionPreference = 'Continue'
        Remove-Variable -Name azunotePipeName, azunoteToken, azunotePipe, azunoteWriter, azunoteReader, azunoteRequest, azunoteEntry -ErrorAction SilentlyContinue
        """;

    /// <summary>
    /// The encoding both a cold and a waiting process set up before anything
    /// is read or written. The input encoding matters because Azunote writes
    /// standard input as UTF-8 while the console code page is whatever the
    /// machine was installed with; setting it is best effort because a process
    /// without a console cannot always change it.
    /// </summary>
    private const string EncodingPrologue = """
        $OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        [Console]::OutputEncoding = $OutputEncoding
        try
        {
            [Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
        }
        catch
        {
        }
        """;

    /// <summary>
    /// Runs the configured command in `$azunoteScript`, given whether standard
    /// input was configured in `$azunoteHasInput`. The command is one command
    /// line, and the semantics being reproduced are those of
    /// `Get-Content document.txt | &lt;command line&gt;`: standard input is what
    /// `Get-Content` would have produced, and it reaches the line as its lines.
    ///
    /// - A line that names `$input` gets the lines there as an array, whether
    ///   it names it directly or wraps the work in `&amp; { ... }`. PowerShell
    ///   would hand a script block its own one-shot enumerator, which neither
    ///   `-join` nor `.Count` nor a second pass can work with, so the block is
    ///   run with `$input` bound instead. Everything an enumerator supports,
    ///   such as `$input | Sort-Object`, still works.
    /// - A line that names no `$input` and is one pipeline starting with a
    ///   command is stepped, so the lines flow into that pipeline exactly as
    ///   they would in `Get-Content document.txt | Sort-Object`.
    /// - Anything else runs with standard input unread, so a line that reads it
    ///   itself with `[Console]::In.ReadToEnd()` sees every byte, line endings
    ///   and a trailing newline included.
    ///
    /// What the line produces is written here rather than by the host, which
    /// would end it with a newline the line never produced and which an output
    /// action such as `replaceSelection` would then insert. Strings are written
    /// as they are, joined by the newline standard input used; anything else is
    /// formatted the way the host would format it, without the trailing newline.
    /// A line that writes to `[Console]::Out` itself bypasses all of this and
    /// keeps every byte it wrote, and what a line produced before it called
    /// `exit` is still written.
    ///
    /// The syntax is limited to what both PowerShell 7 and Windows PowerShell
    /// 5.1 accept, and avoids double quotes so that it survives every way it is
    /// handed to the interpreter.
    /// </summary>
    private const string RunScript = """
        $azunoteNewLine = [Environment]::NewLine
        $azunoteBlock = [scriptblock]::Create($azunoteScript)
        $azunoteWantsInput = $azunoteHasInput -and $azunoteScript -match '\$input'
        $azunoteBody = $null
        $azunotePipeline = $null
        $azunoteStatement = $null
        $azunoteAst = $azunoteBlock.Ast
        if ($azunoteHasInput -and $null -eq $azunoteAst.ParamBlock -and $null -eq $azunoteAst.BeginBlock -and $null -eq $azunoteAst.ProcessBlock -and $null -ne $azunoteAst.EndBlock -and $azunoteAst.EndBlock.Statements.Count -eq 1 -and $azunoteAst.EndBlock.Statements[0] -is [System.Management.Automation.Language.PipelineAst])
        {
            $azunoteStatement = $azunoteAst.EndBlock.Statements[0]
        }

        if ($azunoteWantsInput)
        {
            # The line is run with $input bound to the lines. When it is only a
            # call to a script block, that block is what runs, because a block
            # invoked with & would get an $input of its own instead.
            $azunoteBody = $azunoteScript
            if ($null -ne $azunoteStatement -and $azunoteStatement.PipelineElements.Count -eq 1 -and $azunoteStatement.PipelineElements[0] -is [System.Management.Automation.Language.CommandAst])
            {
                $azunoteCommand = $azunoteStatement.PipelineElements[0]
                if ($azunoteCommand.InvocationOperator -eq [System.Management.Automation.Language.TokenKind]::Ampersand -and $azunoteCommand.CommandElements.Count -eq 1 -and $azunoteCommand.CommandElements[0] -is [System.Management.Automation.Language.ScriptBlockExpressionAst])
                {
                    $azunoteInner = $azunoteCommand.CommandElements[0].ScriptBlock
                    if ($null -eq $azunoteInner.ParamBlock -and $null -eq $azunoteInner.BeginBlock -and $null -eq $azunoteInner.ProcessBlock -and $null -ne $azunoteInner.EndBlock)
                    {
                        $azunoteBody = $azunoteInner.EndBlock.Extent.Text
                    }
                }
            }
        }
        elseif ($azunoteHasInput -and $null -ne $azunoteStatement -and $azunoteStatement.PipelineElements[0] -is [System.Management.Automation.Language.CommandAst])
        {
            try
            {
                $azunotePipeline = $azunoteBlock.GetSteppablePipeline()
            }
            catch
            {
                $azunotePipeline = $null
            }
        }

        $azunoteLines = New-Object -TypeName System.Collections.Generic.List[string]
        if ($null -ne $azunotePipeline -or $null -ne $azunoteBody)
        {
            $azunoteStandardInput = New-Object -TypeName System.IO.StreamReader -ArgumentList ([Console]::OpenStandardInput()), ([System.Text.UTF8Encoding]::new($false))
            $azunoteText = $azunoteStandardInput.ReadToEnd()
            $azunoteStandardInput.Dispose()
            foreach ($azunoteLine in [regex]::Split($azunoteText, '\r\n|\r|\n'))
            {
                $azunoteLines.Add($azunoteLine)
            }

            if ($azunoteLines.Count -gt 0 -and $azunoteLines[$azunoteLines.Count - 1] -eq '')
            {
                $azunoteLines.RemoveAt($azunoteLines.Count - 1)
            }

            $azunoteMatch = [regex]::Match($azunoteText, '\r\n|\r|\n')
            if ($azunoteMatch.Success)
            {
                $azunoteNewLine = $azunoteMatch.Value
            }
        }

        $azunoteInputLines = $azunoteLines.ToArray()
        $azunoteResult = New-Object -TypeName System.Collections.Generic.List[object]
        try
        {
            # The output is written here rather than by the host, which would end it
            # with a newline the command never produced. A command that writes to
            # [Console]::Out itself bypasses this and keeps every byte it wrote.
            & {
                if ($null -ne $azunotePipeline)
                {
                    $azunotePipeline.Begin($true)
                    try
                    {
                        foreach ($azunoteItem in $azunoteInputLines)
                        {
                            $azunotePipeline.Process($azunoteItem)
                        }
                    }
                    finally
                    {
                        $azunotePipeline.End()
                    }
                }
                elseif ($null -ne $azunoteBody)
                {
                    & ([scriptblock]::Create('$input = $azunoteInputLines' + [Environment]::NewLine + $azunoteBody))
                }
                else
                {
                    & $azunoteBlock
                }
            } | ForEach-Object { $azunoteResult.Add($_) }
        }
        finally
        {
            if ($azunoteResult.Count -gt 0)
            {
                $azunoteStrings = $true
                foreach ($azunoteItem in $azunoteResult)
                {
                    if ($azunoteItem -isnot [string])
                    {
                        $azunoteStrings = $false
                        break
                    }
                }

                if ($azunoteStrings)
                {
                    [Console]::Out.Write(($azunoteResult -join $azunoteNewLine))
                }
                else
                {
                    [Console]::Out.Write((($azunoteResult | Out-String -Width 4096) -replace '(\r\n|\r|\n)+$', ''))
                }
            }
        }
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
