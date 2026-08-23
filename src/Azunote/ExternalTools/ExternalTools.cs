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
        string arguments = "",
        ExternalToolInputMode inputMode = ExternalToolInputMode.None,
        ExternalToolOutputMode outputMode = ExternalToolOutputMode.Ignore,
        string? workingDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        FileName = fileName;
        Arguments = arguments;
        InputMode = inputMode;
        OutputMode = outputMode;
        WorkingDirectory = workingDirectory;
    }

    public string FileName { get; }

    public string Arguments { get; }

    public ExternalToolInputMode InputMode { get; }

    public ExternalToolOutputMode OutputMode { get; }

    public string? WorkingDirectory { get; }
}

public sealed record ExternalToolContext
{
    private static readonly Regex PlaceholderPattern = new(
        @"\$\{(?<name>[^{}]+)\}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public ExternalToolContext(
        string? filePath,
        string document,
        string selection,
        int lineNumber = 1,
        int columnNumber = 1)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selection);
        if (lineNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(lineNumber));
        }

        if (columnNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(columnNumber));
        }

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

    public string UserHome
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

        return PlaceholderPattern.Replace(value, match =>
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
    public async Task<ExternalToolResult> RunAsync(
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
            Arguments = context.Expand(definition.Arguments),
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

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

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
            _ => throw new ArgumentOutOfRangeException(nameof(definition.OutputMode))
        };
    }
}
