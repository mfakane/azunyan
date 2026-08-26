using System.Diagnostics;
using System.Text.RegularExpressions;
using Azunyan.Core;
using Azunyan.Syntax;
using Windows.System;

namespace Azunote;

public enum ExternalToolInputMode
{
    None,
    FilePath,
    Document,
    Selection
}

public enum ExternalToolOutputMode
{
    Ignore,
    ReplaceDocument,
    ReplaceSelection,
    NewDocument,
    ReloadFile
}

public sealed record ExternalToolShortcut(
    VirtualKey Key,
    VirtualKeyModifiers Modifiers)
{
    public static bool TryParse(
        string? value,
        out ExternalToolShortcut? shortcut)
    {
        shortcut = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(
            '+',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var modifiers = VirtualKeyModifiers.None;
        foreach (var modifier in parts[..^1])
        {
            switch (modifier.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= VirtualKeyModifiers.Control;
                    break;
                case "alt":
                case "menu":
                    modifiers |= VirtualKeyModifiers.Menu;
                    break;
                case "shift":
                    modifiers |= VirtualKeyModifiers.Shift;
                    break;
                case "win":
                case "windows":
                    modifiers |= VirtualKeyModifiers.Windows;
                    break;
                default:
                    return false;
            }
        }

        var keyName = parts[^1];
        if (string.Equals(keyName, "Esc", StringComparison.OrdinalIgnoreCase))
        {
            keyName = nameof(VirtualKey.Escape);
        }

        if (!Enum.TryParse<VirtualKey>(keyName, ignoreCase: true, out var key)
            || key == VirtualKey.None)
        {
            return false;
        }

        shortcut = new ExternalToolShortcut(key, modifiers);
        return true;
    }
}

/// <summary>
/// A declarative external process invocation. Arguments may contain the
/// placeholders exposed by <see cref="ExternalToolContext"/>. Text payloads
/// are normally better passed through stdin so quoting remains the tool's
/// concern.
/// </summary>
public sealed record ExternalToolDefinition
{
    public ExternalToolDefinition(
        string fileName,
        string[]? arguments = null,
        ExternalToolInputMode inputMode = ExternalToolInputMode.None,
        ExternalToolOutputMode outputMode = ExternalToolOutputMode.Ignore,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        string? definitionDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        FileName = fileName;
        Arguments = arguments ?? [];
        InputMode = inputMode;
        OutputMode = outputMode;
        WorkingDirectory = workingDirectory;
        Environment = environment ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DefinitionDirectory = string.IsNullOrWhiteSpace(definitionDirectory)
            ? null
            : Path.GetFullPath(definitionDirectory);
    }

    public string FileName { get; }

    public string[] Arguments { get; }

    public ExternalToolInputMode InputMode { get; }

    public ExternalToolOutputMode OutputMode { get; }

    public string? WorkingDirectory { get; }

    public IReadOnlyDictionary<string, string> Environment { get; }

    public string? DefinitionDirectory { get; }

    public static string[] ParseArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return [];

        var result = new List<string>();
        var tokens = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var quoted = false;

        foreach (var token in tokens)
        {
            if (token.StartsWith('"') && token.EndsWith('"'))
            {
                result.Add(token[1..^1]);
            }
            else if (token.StartsWith('"'))
            {
                quoted = true;
                result.Add(token[1..]);
            }
            else if (token.EndsWith('"'))
            {
                quoted = false;
                result.Add(token[..^1]);
            }
            else if (quoted)
            {
                result[^1] += " " + token;
            }
            else
            {
                result.Add(token);
            }
        }

        return result.ToArray();
    }
}

public sealed partial record ExternalToolContext
{
    [GeneratedRegex(@"\$\{(?<name>[^{}]+)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    public ExternalToolContext(
        string? filePath,
        string document,
        string selection,
        int lineNumber = 1,
        int columnNumber = 1)
        : this(
            filePath,
            filePath,
            document,
            selection,
            lineNumber,
            columnNumber,
            selectionStart: new LineColumn(lineNumber - 1, columnNumber - 1),
            selectionEnd: new LineColumn(lineNumber - 1, columnNumber - 1))
    {
    }

    public ExternalToolContext(
        string? documentFilePath,
        string? executionFilePath,
        string document,
        string selection,
        int lineNumber = 1,
        int columnNumber = 1,
        string? languageId = null,
        string? toolDirectory = null,
        TextEncodingKind encoding = TextEncodingKind.Utf8,
        LineEndingKind lineEnding = LineEndingKind.Lf,
        bool isDirty = false,
        LineColumn? selectionStart = null,
        LineColumn? selectionEnd = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentOutOfRangeException.ThrowIfLessThan(lineNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(columnNumber, 1);

        DocumentFilePath = string.IsNullOrWhiteSpace(documentFilePath)
            ? null
            : Path.GetFullPath(documentFilePath);
        ExecutionFilePath = string.IsNullOrWhiteSpace(executionFilePath)
            ? null
            : Path.GetFullPath(executionFilePath);
        ToolDirectory = string.IsNullOrWhiteSpace(toolDirectory)
            ? null
            : Path.GetFullPath(toolDirectory);
        Document = document;
        Selection = selection;
        LineNumber = lineNumber;
        ColumnNumber = columnNumber;
        LanguageId = languageId ?? string.Empty;
        Encoding = encoding;
        LineEnding = lineEnding;
        IsDirty = isDirty;
        SelectionStart = selectionStart ?? new LineColumn(lineNumber - 1, columnNumber - 1);
        SelectionEnd = selectionEnd ?? SelectionStart;
    }

    public string? FilePath => ExecutionFilePath;

    public string? ExecutionFilePath { get; }

    public string? FileDir => FilePath is null ? null : Path.GetDirectoryName(FilePath);

    public string? FileName => FilePath is null ? null : Path.GetFileName(FilePath);

    public string? FileStem => FileName is null ? null : Path.GetFileNameWithoutExtension(FileName);

    public string? FileExtension => FileName is null ? null : Path.GetExtension(FileName);

    public string? DocumentFilePath { get; }

    public string? DocumentDir => DocumentFilePath is null
        ? null
        : Path.GetDirectoryName(DocumentFilePath);

    public string? DocumentFileName => DocumentFilePath is null
        ? null
        : Path.GetFileName(DocumentFilePath);

    public string? DocumentStem => DocumentFileName is null
        ? null
        : Path.GetFileNameWithoutExtension(DocumentFileName);

    public string? DocumentExtension => DocumentFileName is null
        ? null
        : Path.GetExtension(DocumentFileName);

    public string? TempFile => ExecutionFilePath is null ||
        string.Equals(DocumentFilePath, ExecutionFilePath, StringComparison.OrdinalIgnoreCase)
            ? null
            : ExecutionFilePath;

    public string? ToolDirectory { get; }

    public string Document { get; }

    public string Selection { get; }

    public int LineNumber { get; }

    public int ColumnNumber { get; }

    public string LanguageId { get; }

    public TextEncodingKind Encoding { get; }

    public LineEndingKind LineEnding { get; }

    public bool IsDirty { get; }

    public LineColumn SelectionStart { get; }

    public LineColumn SelectionEnd { get; }

    public static string UserHome
    {
        get
        {
            var specialFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return !string.IsNullOrWhiteSpace(specialFolder)
                ? specialFolder
                : Environment.GetEnvironmentVariable("USERPROFILE")
                    ?? Environment.GetEnvironmentVariable("HOME")
                    ?? string.Empty;
        }
    }

    public string Expand(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return PlaceholderPattern().Replace(value, match =>
        {
            var name = match.Groups["name"].Value;
            if (name.StartsWith("env:", StringComparison.Ordinal))
            {
                return Environment.GetEnvironmentVariable(name[4..]) ?? string.Empty;
            }

            return name switch
            {
                "file" => FilePath ?? string.Empty,
                "filePath" => FilePath ?? string.Empty,
                "executionFile" => ExecutionFilePath ?? string.Empty,
                "fileDir" => FileDir ?? string.Empty,
                "fileName" => FileName ?? string.Empty,
                "fileStem" => FileStem ?? string.Empty,
                "fileExtension" => FileExtension ?? string.Empty,
                "documentFile" => DocumentFilePath ?? string.Empty,
                "documentDir" => DocumentDir ?? string.Empty,
                "documentName" => DocumentFileName ?? string.Empty,
                "documentStem" => DocumentStem ?? string.Empty,
                "documentExtension" => DocumentExtension ?? string.Empty,
                "tempFile" => TempFile ?? string.Empty,
                "toolDir" => ToolDirectory ?? string.Empty,
                "document" => Document,
                "selection" => Selection,
                "userHome" => UserHome,
                "languageId" => LanguageId,
                "encoding" => Encoding.ToString(),
                "lineEnding" => LineEnding.ToString(),
                "platform" => OperatingSystem.IsWindows() ? "windows" :
                    OperatingSystem.IsLinux() ? "linux" :
                    OperatingSystem.IsMacOS() ? "macos" : "unknown",
                "architecture" => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
                "lineNumber" => LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "columnNumber" => ColumnNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "selectionStartLine" => (SelectionStart.Line + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "selectionStartColumn" => (SelectionStart.Column + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "selectionEndLine" => (SelectionEnd.Line + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "selectionEndColumn" => (SelectionEnd.Column + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => match.Value
            };
        });
    }

    public string[] Expand(string[] arguments) => [.. arguments.Select(Expand)];

    public string GetInput(ExternalToolInputMode inputMode) => inputMode switch
    {
        ExternalToolInputMode.None => string.Empty,
        ExternalToolInputMode.FilePath => FilePath
            ?? throw new InvalidOperationException(
                "The external tool requires a file-backed document."),
        ExternalToolInputMode.Document => Document,
        ExternalToolInputMode.Selection => Selection,
        _ => throw new ArgumentOutOfRangeException(nameof(inputMode))
    };

}

public sealed record ExternalToolMenuState(
    bool IsVisible,
    bool IsEnabled,
    string? DisabledReason = null);

internal enum ExternalToolLaunchKind
{
    Direct,
    CommandShell,
    PowerShell
}

internal sealed record ExternalToolLaunchPlan(
    string RequestedCommand,
    string ResolvedPath,
    string LauncherPath,
    ExternalToolLaunchKind Kind)
{
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
            case ExternalToolLaunchKind.PowerShell:
                startInfo.ArgumentList.Add("-NoLogo");
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-NonInteractive");
                startInfo.ArgumentList.Add("-File");
                startInfo.ArgumentList.Add(ResolvedPath);
                AddAll(startInfo, arguments);
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
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
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
        return string.Join(' ', commandParts);
    }

    private static string QuoteCommandShellArgument(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}

internal static class ExternalToolLaunchResolver
{
    public static ExternalToolLaunchPlan? Resolve(
        string command,
        string? definitionDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

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

    private static string? ResolvePowerShell() =>
        ResolveExecutable("pwsh.exe", null)
        ?? ResolveExecutable("powershell.exe", null);
}

public static class ExternalToolAvailability
{
    public static ExternalToolMenuState Evaluate(
        ExternalToolSettings settings,
        ExternalToolContext context)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);

        var conditionFailure = CheckConditions(settings.When ?? new(), context);
        var visibility = ParseVisibility(settings.Visibility);
        if (conditionFailure is not null)
        {
            return visibility == ExternalToolVisibility.Always
                ? new ExternalToolMenuState(true, false, conditionFailure)
                : new ExternalToolMenuState(false, false, conditionFailure);
        }

        var definition = settings.ToDefinition();
        var command = context.Expand(definition.FileName);
        var launchPlan = ExternalToolLaunchResolver.Resolve(
            command,
            definition.DefinitionDirectory);
        if (launchPlan is null)
        {
            var reason = $"Command '{command}' was not found.";
            return visibility == ExternalToolVisibility.Always
                ? new ExternalToolMenuState(true, false, reason)
                : new ExternalToolMenuState(false, false, reason);
        }

        return new ExternalToolMenuState(true, true);
    }

    private static string? CheckConditions(
        ExternalToolWhenSettings when,
        ExternalToolContext context)
    {
        var documentPath = context.DocumentFilePath;
        var extensions = when.Extensions ?? [];
        var patterns = when.Patterns ?? [];

        if (extensions.Length > 0
            && (documentPath is null || !extensions.Any(extension =>
                string.Equals(
                    NormalizeExtension(extension),
                    context.DocumentExtension,
                    StringComparison.OrdinalIgnoreCase))))
        {
            return "The current document has an unsupported file extension.";
        }

        if (patterns.Length > 0
            && (documentPath is null
                || SyntaxLanguageDefinition.GetPatternMatchScore(documentPath, patterns) < 0))
        {
            return "The current document does not match the configured file pattern.";
        }

        if (when.Languages is { Length: > 0 }
            && !when.Languages.Contains(context.LanguageId, StringComparer.OrdinalIgnoreCase))
        {
            return "The current language mode is not supported by this tool.";
        }

        var fileCondition = ExternalToolEnumValues.Parse<ExternalToolFileCondition>(
            when.File,
            "when.file");
        if (fileCondition == ExternalToolFileCondition.Backed && documentPath is null)
        {
            return "This tool requires a file-backed document.";
        }

        if (fileCondition == ExternalToolFileCondition.Untitled && documentPath is not null)
        {
            return "This tool is only available for untitled documents.";
        }

        var selectionCondition = ExternalToolEnumValues.Parse<ExternalToolSelectionCondition>(
            when.Selection,
            "when.selection");
        if (selectionCondition == ExternalToolSelectionCondition.Empty
            && context.Selection.Length > 0)
        {
            return "This tool requires an empty selection.";
        }

        if (selectionCondition == ExternalToolSelectionCondition.NonEmpty
            && context.Selection.Length == 0)
        {
            return "This tool requires a selection.";
        }

        var documentCondition = ExternalToolEnumValues.Parse<ExternalToolDocumentCondition>(
            when.Document,
            "when.document");
        if (documentCondition == ExternalToolDocumentCondition.Clean && context.IsDirty)
        {
            return "This tool requires a clean document.";
        }

        if (documentCondition == ExternalToolDocumentCondition.Dirty && !context.IsDirty)
        {
            return "This tool requires unsaved document changes.";
        }

        if (when.Os is { Length: > 0 }
            && !when.Os.Contains(GetOperatingSystemName(), StringComparer.OrdinalIgnoreCase))
        {
            return "This tool is not available on the current operating system.";
        }

        return null;
    }

    private static string NormalizeExtension(string extension)
    {
        extension = extension.Trim();
        if (extension.StartsWith('*'))
        {
            extension = extension[1..];
        }

        return extension.StartsWith('.') ? extension : $".{extension}";
    }

    private static ExternalToolVisibility ParseVisibility(string? value) =>
        ExternalToolEnumValues.Parse<ExternalToolVisibility>(
            value,
            nameof(ExternalToolVisibility));

    private static string GetOperatingSystemName() =>
        OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsLinux() ? "linux" :
        OperatingSystem.IsMacOS() ? "macos" :
        "unknown";
}

public sealed record ExternalToolResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// Runs a tool directly when possible and uses the appropriate Windows
/// launcher for command and PowerShell scripts. Arguments remain separate for
/// direct and PowerShell launches; command-shell arguments are quoted into the
/// single command string required by cmd.exe.
/// </summary>
public sealed class ExternalToolRunner
{
    public static async Task<ExternalToolResult> RunAsync(
        ExternalToolDefinition definition,
        ExternalToolContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(context);

        var input = definition.InputMode == ExternalToolInputMode.None
            ? null
            : context.GetInput(definition.InputMode);
        var command = context.Expand(definition.FileName);
        var launchPlan = ExternalToolLaunchResolver.Resolve(
            command,
            definition.DefinitionDirectory);
        if (launchPlan is null)
        {
            var exception = new InvalidOperationException(
                $"Could not resolve external tool '{command}'.");
            ErrorReporter.LogException(
                "External tool launch could not be resolved",
                new InvalidOperationException(
                    $"Command: {command}{Environment.NewLine}"
                    + $"DefinitionDirectory: {definition.DefinitionDirectory}",
                    exception));
            throw exception;
        }

        var arguments = context.Expand(definition.Arguments);
        var startInfo = new ProcessStartInfo
        {
            FileName = launchPlan.LauncherPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = ResolveWorkingDirectory(definition, context)
        };
        foreach (var environmentVariable in definition.Environment)
        {
            startInfo.Environment[environmentVariable.Key] = context.Expand(environmentVariable.Value);
        }

        launchPlan.AddArguments(startInfo, arguments);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(
                    $"Could not start external tool: {startInfo.FileName}");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorReporter.LogException(
                $"External tool launch failed: {startInfo.FileName}",
                new InvalidOperationException(
                    DescribeLaunch(definition, launchPlan, startInfo, arguments, context),
                    exception));
            throw;
        }

        try
        {

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            if (input is null)
            {
                process.StandardInput.Close();
            }
            else
            {
                await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken);
                await process.StandardInput.FlushAsync(cancellationToken);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(cancellationToken);
            return new ExternalToolResult(
                process.ExitCode,
                await outputTask,
                await errorTask);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static string DescribeLaunch(
        ExternalToolDefinition definition,
        ExternalToolLaunchPlan launchPlan,
        ProcessStartInfo startInfo,
        string[] arguments,
        ExternalToolContext context)
    {
        var formattedArguments = arguments.Length > 0
            ? string.Join(
                Environment.NewLine,
                arguments.Select(argument =>
                    $"  {MaskDocumentText(argument, context)}"))
            : "(none)";
        var environmentVariables = definition.Environment.Count == 0
            ? "(none)"
            : string.Join(", ", definition.Environment.Keys.Order(StringComparer.OrdinalIgnoreCase));

        return $"RequestedCommand: {launchPlan.RequestedCommand}{Environment.NewLine}"
            + $"ResolvedPath: {launchPlan.ResolvedPath}{Environment.NewLine}"
            + $"Launcher: {launchPlan.LauncherPath}{Environment.NewLine}"
            + $"LaunchKind: {launchPlan.Kind}{Environment.NewLine}"
            + $"FileName: {startInfo.FileName}{Environment.NewLine}"
            + $"Arguments:{Environment.NewLine}{formattedArguments}{Environment.NewLine}"
            + $"WorkingDirectory: {startInfo.WorkingDirectory}{Environment.NewLine}"
            + $"InputMode: {definition.InputMode}{Environment.NewLine}"
            + $"OutputMode: {definition.OutputMode}{Environment.NewLine}"
            + $"EnvironmentVariables: {environmentVariables}";
    }

    private static string MaskDocumentText(
        string argument,
        ExternalToolContext context)
    {
        if (!string.IsNullOrEmpty(context.Document))
        {
            argument = argument.Replace(
                context.Document,
                "<document text>",
                StringComparison.Ordinal);
        }

        if (!string.IsNullOrEmpty(context.Selection))
        {
            argument = argument.Replace(
                context.Selection,
                "<selection text>",
                StringComparison.Ordinal);
        }

        return argument;
    }

    private static string ResolveWorkingDirectory(
        ExternalToolDefinition definition,
        ExternalToolContext context)
    {
        var configured = definition.WorkingDirectory is null
            ? context.DocumentDir ?? context.FileDir
            : context.Expand(definition.WorkingDirectory);

        if (!string.IsNullOrWhiteSpace(configured)
            && !Path.IsPathRooted(configured)
            && !string.IsNullOrWhiteSpace(definition.DefinitionDirectory))
        {
            configured = Path.GetFullPath(Path.Combine(definition.DefinitionDirectory, configured));
        }

        return string.IsNullOrWhiteSpace(configured) || !Directory.Exists(configured)
            ? Environment.CurrentDirectory
            : configured;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between cancellation and cleanup.
        }
    }
}

public sealed record ExternalToolOutput(
    string? ReplacementText,
    bool ReloadFile,
    string? Error)
{
    public bool IsSuccess => Error is null;
}

public static class ExternalToolOutputInterpreter
{
    public static ExternalToolOutput Interpret(
        ExternalToolDefinition definition,
        ExternalToolResult result)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(result);

        if (!result.Succeeded)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? $"External tool exited with code {result.ExitCode}."
                : result.StandardError.Trim();
            return new ExternalToolOutput(null, false, detail);
        }

        return definition.OutputMode switch
        {
            ExternalToolOutputMode.Ignore => new ExternalToolOutput(null, false, null),
            ExternalToolOutputMode.ReplaceDocument =>
                new ExternalToolOutput(result.StandardOutput, false, null),
            ExternalToolOutputMode.ReplaceSelection =>
                new ExternalToolOutput(result.StandardOutput, false, null),
            ExternalToolOutputMode.NewDocument =>
                new ExternalToolOutput(result.StandardOutput, false, null),
            ExternalToolOutputMode.ReloadFile =>
                new ExternalToolOutput(null, true, null),
            _ => throw new ArgumentOutOfRangeException(nameof(definition), "Unsupported output mode.")
        };
    }
}

/// <summary>
/// Discovers external tool definitions below a settings directory. Directory
/// names become menu folders, while files ending in <c>.tool.toml</c> are
/// tool leaves. A directory ending in <c>.tool</c> with a manifest.toml file
/// is a bundled tool leaf and is not traversed as a menu folder.
/// </summary>
public static class ExternalToolDiscovery
{
    private const string ToolFileSuffix = ".tool.toml";
    private const string ToolDirectorySuffix = ".tool";
    private const string ManifestFileName = "manifest.toml";

    public static async Task<ExternalToolCatalog> LoadAsync(
        string toolsDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolsDirectory);

        var fullDirectory = Path.GetFullPath(toolsDirectory);
        if (!Directory.Exists(fullDirectory))
        {
            return new ExternalToolCatalog([], []);
        }

        var tools = new List<ExternalToolSettings>();
        var menu = await LoadDirectoryAsync(fullDirectory, tools, cancellationToken);
        return new ExternalToolCatalog(menu, tools);
    }

    private static async Task<IReadOnlyList<ExternalToolMenuNode>> LoadDirectoryAsync(
        string directory,
        ICollection<ExternalToolSettings> tools,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nodes = new List<ExternalToolMenuNode>();
        foreach (var childDirectory in Directory.EnumerateDirectories(directory)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var childDirectoryName = Path.GetFileName(childDirectory);
            var manifestPath = Path.Combine(childDirectory, ManifestFileName);
            if (childDirectoryName.EndsWith(ToolDirectorySuffix, StringComparison.OrdinalIgnoreCase)
                && File.Exists(manifestPath))
            {
                var tool = await LoadToolAsync(
                    manifestPath,
                    TrimSuffix(childDirectoryName, ToolDirectorySuffix),
                    childDirectory,
                    cancellationToken);
                tools.Add(tool);
                nodes.Add(new ExternalToolMenuNode(tool.Name, tool));
                continue;
            }

            var children = await LoadDirectoryAsync(childDirectory, tools, cancellationToken);
            if (children.Count > 0)
            {
                nodes.Add(new ExternalToolMenuNode(childDirectoryName, children: children));
            }
        }

        foreach (var toolPath in Directory.EnumerateFiles(directory)
                     .Where(path => Path.GetFileName(path).EndsWith(
                         ToolFileSuffix,
                         StringComparison.OrdinalIgnoreCase))
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fallbackName = TrimSuffix(Path.GetFileName(toolPath), ToolFileSuffix);
            var tool = await LoadToolAsync(
                toolPath,
                fallbackName,
                directory,
                cancellationToken);
            tools.Add(tool);
            nodes.Add(new ExternalToolMenuNode(tool.Name, tool));
        }

        return nodes;
    }

    private static async Task<ExternalToolSettings> LoadToolAsync(
        string path,
        string fallbackName,
        string definitionDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            var tool = await SettingsFileService.DeserializeAsync<ExternalToolSettings>(
                path,
                AzunoteTomlSerializerContext.Default.ExternalToolSettings,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(tool.Name))
            {
                tool.Name = fallbackName;
            }

            tool.DefinitionDirectory = Path.GetFullPath(definitionDirectory);
            tool.Validate();
            return tool;
        }
        catch (SettingsFileException exception)
        {
            throw new SettingsFileException(
                $"Could not load external tool definition '{path}': {exception.Message}",
                exception);
        }
    }

    private static string TrimSuffix(string value, string suffix) =>
        value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? value[..^suffix.Length]
            : value;
}
