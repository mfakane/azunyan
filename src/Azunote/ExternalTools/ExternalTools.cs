using System.Diagnostics;
using System.Text.RegularExpressions;

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

/// <summary>
/// A declarative external process invocation. Arguments may contain
/// <c>${file}</c>, <c>${fileDir}</c>, <c>${fileName}</c>,
/// <c>${document}</c>, <c>${selection}</c>, <c>${userHome}</c>,
/// <c>${lineNumber}</c>, and <c>${columnNumber}</c> placeholders. Environment
/// variables use <c>${env:NAME}</c>. Text payloads are normally better passed
/// through stdin so quoting remains the tool's concern.
/// </summary>
public sealed record ExternalToolDefinition
{
    public ExternalToolDefinition(
        string fileName,
        string[]? arguments = null,
        ExternalToolInputMode inputMode = ExternalToolInputMode.None,
        ExternalToolOutputMode outputMode = ExternalToolOutputMode.Ignore,
        string? workingDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        FileName = fileName;
        Arguments = arguments ?? [];
        InputMode = inputMode;
        OutputMode = outputMode;
        WorkingDirectory = workingDirectory;
    }

    public string FileName { get; }

    public string[] Arguments { get; }

    public ExternalToolInputMode InputMode { get; }

    public ExternalToolOutputMode OutputMode { get; }

    public string? WorkingDirectory { get; }

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
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentOutOfRangeException.ThrowIfLessThan(lineNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(columnNumber, 1);

        FilePath = string.IsNullOrWhiteSpace(filePath)
            ? null
            : Path.GetFullPath(filePath);
        Document = document;
        Selection = selection;
        LineNumber = lineNumber;
        ColumnNumber = columnNumber;
    }

    public string? FilePath { get; }

    public string? FileDir => FilePath is null ? null : Path.GetDirectoryName(FilePath);

    public string? FileName => FilePath is null ? null : Path.GetFileName(FilePath);

    public string Document { get; }

    public string Selection { get; }

    public int LineNumber { get; }

    public int ColumnNumber { get; }

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
                "fileDir" => FileDir ?? string.Empty,
                "fileName" => FileName ?? string.Empty,
                "document" => Document,
                "selection" => Selection,
                "userHome" => UserHome,
                "lineNumber" => LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "columnNumber" => ColumnNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
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

public sealed record ExternalToolResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// Runs a tool without invoking a shell. This makes arguments predictable and
/// avoids turning document text into shell syntax. A caller can explicitly use
/// PowerShell, cmd, Python, or another shell as the process file when needed.
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
        var startInfo = new ProcessStartInfo
        {
            FileName = context.Expand(definition.FileName),
            Arguments = string.Join(" ", context.Expand(definition.Arguments).Select(x => x.Contains(' ') ? $"\"{x}\"" : x)),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = ResolveWorkingDirectory(definition, context)
        };

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(
                    $"Could not start external tool: {startInfo.FileName}");
            }

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

    private static string ResolveWorkingDirectory(
        ExternalToolDefinition definition,
        ExternalToolContext context)
    {
        var configured = definition.WorkingDirectory is null
            ? context.FileDir
            : context.Expand(definition.WorkingDirectory);

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
