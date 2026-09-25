using System.Text.RegularExpressions;
using Windows.System;

namespace Azunote;

public enum ExternalToolInputMode
{
    None,
    FilePath,
    Document,
    Selection
}

public enum ExternalToolCommandMode
{
    Executable,
    Cmd,
    Pwsh
}

public enum ExternalToolPerMode
{
    None,
    Line,
    Regex
}

public enum ExternalToolOutputMode
{
    Ignore,
    ReplaceDocument,
    ReplaceSelection,
    NewDocument,
    ReloadFile,
    ShowCompletion
}

public sealed record ExternalToolPer(
    ExternalToolPerMode Mode,
    string? Pattern = null)
{
    public static ExternalToolPer Parse(
        string? value,
        string propertyName = "per")
    {
        value = string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();
        if (string.Equals(value, "none", StringComparison.Ordinal))
        {
            return new ExternalToolPer(ExternalToolPerMode.None);
        }

        if (string.Equals(value, "line", StringComparison.Ordinal))
        {
            return new ExternalToolPer(ExternalToolPerMode.Line);
        }

        const string regexPrefix = "regex:";
        if (value.StartsWith(regexPrefix, StringComparison.Ordinal))
        {
            var pattern = value[regexPrefix.Length..];
            if (pattern.Length == 0)
            {
                throw new ArgumentException(
                    $"The {propertyName} regex pattern must not be empty.",
                    propertyName);
            }

            try
            {
                _ = new Regex(pattern, RegexOptions.CultureInvariant);
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException(
                    $"The {propertyName} regex pattern is invalid: {exception.Message}",
                    propertyName,
                    exception);
            }

            return new ExternalToolPer(ExternalToolPerMode.Regex, pattern);
        }

        throw new ArgumentException(
            $"Invalid {propertyName} value '{value}'. Expected none, line, or regex:<pattern>.",
            propertyName);
    }

    public IReadOnlyList<ExternalToolInputPart> GetInputParts(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return Mode switch
        {
            ExternalToolPerMode.None => [new ExternalToolInputPart(input)],
            ExternalToolPerMode.Line => Regex.Split(input, "\\r\\n|\\r|\\n")
                .Select(part => new ExternalToolInputPart(part))
                .ToArray(),
            ExternalToolPerMode.Regex => GetRegexInputParts(
                input,
                Pattern ?? throw new InvalidOperationException("A regex per mode requires a pattern.")),
            _ => throw new InvalidOperationException($"Unsupported per mode: {Mode}.")
        };
    }

    public IReadOnlyList<string> Split(string input) =>
        GetInputParts(input)
            .Select(part => part.Value)
            .ToArray();

    private static ExternalToolInputPart[] GetRegexInputParts(
        string input,
        string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant);
        var groupNumbers = regex.GetGroupNumbers();
        return regex.Matches(input)
            .Select(match =>
            {
                var captures = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var number in groupNumbers)
                {
                    var value = match.Groups[number].Value;
                    captures[number.ToString(System.Globalization.CultureInfo.InvariantCulture)] = value;
                    captures[regex.GroupNameFromNumber(number)] = value;
                }

                return new ExternalToolInputPart(match.Value, captures);
            })
            .ToArray();
    }
}

public sealed record ExternalToolInputPart(
    string Value,
    IReadOnlyDictionary<string, string>? Captures = null);

public sealed record ExternalToolOutputActions(
    ExternalToolOutputMode OnSuccess,
    ExternalToolOutputMode OnFailure)
{
    public ExternalToolOutputMode Select(bool succeeded) =>
        succeeded ? OnSuccess : OnFailure;

    public static ExternalToolOutputActions Ignore { get; } =
        new(ExternalToolOutputMode.Ignore, ExternalToolOutputMode.Ignore);
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
/// concern. <see cref="ExternalToolCommandMode.Cmd"/> and
/// <see cref="ExternalToolCommandMode.Pwsh"/> treat <see cref="FileName"/>
/// as one shell command and do not use arguments.
/// </summary>
public sealed record ExternalToolDefinition
{
    public ExternalToolDefinition(
        string fileName,
        string[]? arguments = null,
        ExternalToolInputMode inputMode = ExternalToolInputMode.None,
        string? per = null,
        string? stdin = null,
        ExternalToolOutputActions? output = null,
        ExternalToolOutputActions? stdout = null,
        ExternalToolOutputActions? stderr = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        string? definitionDirectory = null,
        ExternalToolCommandMode commandMode = ExternalToolCommandMode.Executable,
        ExternalToolStreamChannels stream = ExternalToolStreamChannels.None,
        IReadOnlyList<string>? searchPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (commandMode is not ExternalToolCommandMode.Executable
            && arguments is { Length: > 0 })
        {
            throw new ArgumentException(
                "Shell-command external tools cannot specify arguments.",
                nameof(arguments));
        }

        FileName = fileName;
        Arguments = arguments ?? [];
        CommandMode = commandMode;
        InputMode = inputMode;
        Per = ExternalToolPer.Parse(per);
        Stdin = stdin ?? string.Empty;
        Output = output ?? ExternalToolOutputActions.Ignore;
        Stdout = stdout ?? ExternalToolOutputActions.Ignore;
        Stderr = stderr ?? ExternalToolOutputActions.Ignore;
        Stream = stream;
        if (ExternalToolStreaming.Describe(Stream, Output, Stdout, Stderr) is { } streamError)
        {
            throw new ArgumentException(streamError, nameof(stream));
        }

        WorkingDirectory = workingDirectory;
        SearchPath = searchPath ?? [];
        Environment = environment ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DefinitionDirectory = string.IsNullOrWhiteSpace(definitionDirectory)
            ? null
            : Path.GetFullPath(definitionDirectory);
    }

    public string FileName { get; }

    public string[] Arguments { get; }

    public ExternalToolCommandMode CommandMode { get; }

    public ExternalToolInputMode InputMode { get; }

    public ExternalToolPer Per { get; }

    public string Stdin { get; }

    public ExternalToolOutputActions Output { get; }

    public ExternalToolOutputActions Stdout { get; }

    public ExternalToolOutputActions Stderr { get; }

    /// <summary>
    /// The channels applied while the tool runs rather than after it exits.
    /// </summary>
    public ExternalToolStreamChannels Stream { get; }

    public string? WorkingDirectory { get; }

    public IReadOnlyList<string> SearchPath { get; }

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
