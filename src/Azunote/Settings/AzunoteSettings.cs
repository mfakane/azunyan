using System.Text;
using System.Text.Json;

namespace Azunote;

public sealed class AzunoteSettings
{
    public List<ExternalToolSettings> ExternalTools { get; set; } = new();

    public IReadOnlyList<ExternalToolSettings> Validate()
    {
        ExternalTools ??= new List<ExternalToolSettings>();
        foreach (var tool in ExternalTools)
        {
            if (tool is null)
            {
                throw new SettingsFileException("externalTools cannot contain null entries.");
            }

            tool.Validate();
        }

        return ExternalTools;
    }
}

public sealed class ExternalToolSettings
{
    public string Name { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public string[] Arguments { get; set; } = [];

    public string Input { get; set; } = nameof(ExternalToolInputMode.None);

    public string Output { get; set; } = nameof(ExternalToolOutputMode.Ignore);

    public string? WorkingDirectory { get; set; }

    public ExternalToolDefinition ToDefinition()
    {
        Validate();
        return new ExternalToolDefinition(
            Command,
            Arguments,
            ParseEnum<ExternalToolInputMode>(Input, nameof(Input)),
            ParseEnum<ExternalToolOutputMode>(Output, nameof(Output)),
            WorkingDirectory);
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new SettingsFileException("Each external tool needs a non-empty name.");
        }

        if (string.IsNullOrWhiteSpace(Command))
        {
            throw new SettingsFileException($"External tool '{Name}' needs a command.");
        }

        _ = ParseEnum<ExternalToolInputMode>(Input, nameof(Input));
        _ = ParseEnum<ExternalToolOutputMode>(Output, nameof(Output));
    }

    private static TEnum ParseEnum<TEnum>(string? value, string propertyName)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
        {
            throw new SettingsFileException(
                $"Invalid {propertyName} value '{value}'. Expected a {typeof(TEnum).Name} value.");
        }

        return parsed;
    }
}

public sealed class SettingsFileException : Exception
{
    public SettingsFileException(string message)
        : base(message)
    {
    }

    public SettingsFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public static class SettingsFileService
{
    private const string SettingsDirectoryName = "Azunote";
    private const string SettingsFileName = "settings.json5";
    private const string DefaultSettingsText = """
        {
          // External commands shown under Tools > External Tools.
          // Text values may use ${file}, ${document}, ${selection},
          // ${userHome}, ${lineNumber}, ${columnNumber}, or ${env:NAME}.
          "externalTools": [
            // {
            //   "name": "Run C# File-based app",
            //   "command": "dotnet",
            //   "args": ["run", "--file", "${file}"],
            //   "inputMode": "none",
            //   "outputMode": "newDocument"
            // }
          ],
        }
        """;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string GetDefaultPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }

        return Path.Combine(localAppData, SettingsDirectoryName, SettingsFileName);
    }

    public static async Task EnsureExistsAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (File.Exists(path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            path,
            DefaultSettingsText,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
    }

    public static async Task<AzunoteSettings> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return new AzunoteSettings();
        }

        var text = await File.ReadAllTextAsync(path, cancellationToken);
        try
        {
            var settings = JsonSerializer.Deserialize<AzunoteSettings>(
                Json5Normalizer.Normalize(text),
                SerializerOptions)
                ?? new AzunoteSettings();
            settings.Validate();
            return settings;
        }
        catch (SettingsFileException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new SettingsFileException(
                $"Could not parse settings file '{path}'.",
                exception);
        }
    }

    public static async Task SaveAsync(
        string path,
        AzunoteSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        await File.WriteAllTextAsync(
            path,
            "// Azunote settings. This file uses JSON5 syntax.\n" + json + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
    }

    private static class Json5Normalizer
    {
        public static string Normalize(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            return RemoveTrailingCommas(
                QuoteUnquotedKeys(RemoveCommentsAndConvertStrings(text)));
        }

        private static string RemoveCommentsAndConvertStrings(string text)
        {
            var output = new StringBuilder(text.Length);
            var inDoubleQuotedString = false;
            var inSingleQuotedString = false;
            var escaped = false;

            for (var index = 0; index < text.Length; index++)
            {
                var current = text[index];
                var next = index + 1 < text.Length ? text[index + 1] : '\0';

                if (inSingleQuotedString)
                {
                    if (escaped)
                    {
                        output.Append(ConvertSingleQuotedEscape(current));
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '\'')
                    {
                        output.Append('"');
                        inSingleQuotedString = false;
                    }
                    else
                    {
                        if (current == '"')
                        {
                            output.Append('\\');
                        }

                        output.Append(current);
                    }

                    continue;
                }

                if (inDoubleQuotedString)
                {
                    output.Append(current);
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '"')
                    {
                        inDoubleQuotedString = false;
                    }

                    continue;
                }

                if (current == '/' && next == '/')
                {
                    index += 2;
                    while (index < text.Length && text[index] is not '\r' and not '\n')
                    {
                        index++;
                    }

                    if (index < text.Length)
                    {
                        output.Append(text[index]);
                    }

                    continue;
                }

                if (current == '/' && next == '*')
                {
                    index += 2;
                    while (index + 1 < text.Length
                        && !(text[index] == '*' && text[index + 1] == '/'))
                    {
                        if (text[index] is '\r' or '\n')
                        {
                            output.Append(text[index]);
                        }

                        index++;
                    }

                    index++;
                    continue;
                }

                if (current == '"')
                {
                    inDoubleQuotedString = true;
                }
                else if (current == '\'')
                {
                    output.Append('"');
                    inSingleQuotedString = true;
                    continue;
                }

                output.Append(current);
            }

            if (inSingleQuotedString || inDoubleQuotedString || escaped)
            {
                throw new SettingsFileException("A settings string is not terminated.");
            }

            return output.ToString();
        }

        private static char ConvertSingleQuotedEscape(char value) => value switch
        {
            '\'' => '\'',
            '"' => '"',
            '\\' => '\\',
            '/' => '/',
            'b' => '\b',
            'f' => '\f',
            'n' => '\n',
            'r' => '\r',
            't' => '\t',
            _ => value
        };

        private static string QuoteUnquotedKeys(string text)
        {
            var output = new StringBuilder(text.Length);
            var inString = false;
            var escaped = false;

            for (var index = 0; index < text.Length; index++)
            {
                var current = text[index];
                output.Append(current);
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    continue;
                }

                if (current is not '{' and not ',')
                {
                    continue;
                }

                var whitespaceStart = index + 1;
                var keyStart = whitespaceStart;
                while (keyStart < text.Length && char.IsWhiteSpace(text[keyStart]))
                {
                    keyStart++;
                }

                var keyEnd = keyStart;
                while (keyEnd < text.Length
                    && (char.IsLetterOrDigit(text[keyEnd]) || text[keyEnd] is '_' or '$'))
                {
                    keyEnd++;
                }

                var colon = keyEnd;
                while (colon < text.Length && char.IsWhiteSpace(text[colon]))
                {
                    colon++;
                }

                if (keyEnd > keyStart && colon < text.Length && text[colon] == ':')
                {
                    output.Append(text, whitespaceStart, keyStart - whitespaceStart);
                    output.Append('"');
                    output.Append(text, keyStart, keyEnd - keyStart);
                    output.Append('"');
                    index = keyEnd - 1;
                }
            }

            return output.ToString();
        }

        private static string RemoveTrailingCommas(string text)
        {
            var output = new StringBuilder(text.Length);
            var inString = false;
            var escaped = false;

            for (var index = 0; index < text.Length; index++)
            {
                var current = text[index];
                if (inString)
                {
                    output.Append(current);
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    output.Append(current);
                    continue;
                }

                if (current == ',')
                {
                    var lookahead = index + 1;
                    while (lookahead < text.Length && char.IsWhiteSpace(text[lookahead]))
                    {
                        lookahead++;
                    }

                    if (lookahead < text.Length && text[lookahead] is '}' or ']')
                    {
                        continue;
                    }
                }

                output.Append(current);
            }

            return output.ToString();
        }
    }
}
