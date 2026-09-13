using System.Text;
using Azunyan.Core;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Azunote;

internal interface IEditorConfigResolver
{
    Task<EditorConfigSettings> ResolveAsync(
        string path,
        CancellationToken cancellationToken = default);
}

internal sealed class EditorConfigResolver : IEditorConfigResolver
{
    private const string FileName = ".editorconfig";
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async Task<EditorConfigSettings> ResolveAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return EditorConfigSettings.Empty;
        }

        var configs = new List<ParsedEditorConfig>();
        for (var current = directory;
             !string.IsNullOrWhiteSpace(current);
             current = GetParentDirectory(current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var configPath = Path.Combine(current, FileName);
            if (File.Exists(configPath)
                && await TryReadAsync(configPath, cancellationToken) is { } config)
            {
                configs.Add(config);
                if (config.Root)
                {
                    break;
                }
            }

            var parent = GetParentDirectory(current);
            if (parent is null)
            {
                break;
            }
        }

        if (configs.Count == 0)
        {
            return EditorConfigSettings.Empty;
        }

        configs.Reverse();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var config in configs)
        {
            var relativePath = NormalizePath(Path.GetRelativePath(config.Directory, fullPath));
            foreach (var section in config.Sections)
            {
                if (!EditorConfigGlob.IsMatch(section.Pattern, relativePath, fullPath))
                {
                    continue;
                }

                foreach (var property in section.Properties)
                {
                    if (string.Equals(property.Value, "unset", StringComparison.OrdinalIgnoreCase))
                    {
                        values.Remove(property.Key);
                    }
                    else
                    {
                        values[property.Key] = property.Value;
                    }
                }
            }
        }

        return EditorConfigSettingsParser.Parse(values);
    }

    private static async Task<ParsedEditorConfig?> TryReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var text = await File.ReadAllTextAsync(path, Utf8, cancellationToken);
            return ParsedEditorConfig.Parse(path, text);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or DecoderFallbackException
                or ArgumentException)
        {
            // EditorConfig is advisory. A broken file must not prevent the
            // document itself from opening.
            return null;
        }
    }

    private static string NormalizePath(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static string? GetParentDirectory(string directory)
    {
        var parent = Directory.GetParent(directory)?.FullName;
        return string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase)
            ? null
            : parent;
    }

    private sealed class ParsedEditorConfig
    {
        private ParsedEditorConfig(
            string directory,
            bool root,
            IReadOnlyList<EditorConfigSection> sections)
        {
            Directory = directory;
            Root = root;
            Sections = sections;
        }

        public string Directory { get; }

        public bool Root { get; }

        public IReadOnlyList<EditorConfigSection> Sections { get; }

        public static ParsedEditorConfig Parse(string path, string text)
        {
            var sections = new List<EditorConfigSection>();
            EditorConfigSection? current = null;
            var root = false;
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var rawLine = lineIndex == 0
                    ? lines[lineIndex].TrimStart('\uFEFF')
                    : lines[lineIndex];
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] is '#' or ';')
                {
                    continue;
                }

                if (line[0] == '[' && line[^1] == ']')
                {
                    var pattern = line[1..^1].Trim();
                    current = pattern.Length == 0
                        ? null
                        : new EditorConfigSection(pattern);
                    if (current is not null)
                    {
                        sections.Add(current);
                    }

                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = line[..separator].Trim().ToLowerInvariant();
                var value = RemoveInlineComment(line[(separator + 1)..].Trim());
                if (key.Length == 0 || value.Length == 0)
                {
                    continue;
                }

                if (current is null)
                {
                    if (key == "root"
                        && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        root = true;
                    }

                    continue;
                }

                current.Set(key, value);
            }

            return new ParsedEditorConfig(
                Path.GetDirectoryName(path)!,
                root,
                sections);
        }

        private static string RemoveInlineComment(string value)
        {
            for (var index = 1; index < value.Length; index++)
            {
                if (value[index] is '#' or ';' && char.IsWhiteSpace(value[index - 1]))
                {
                    return value[..index].TrimEnd();
                }
            }

            return value;
        }
    }

    private sealed class EditorConfigSection
    {
        public EditorConfigSection(string pattern)
        {
            Pattern = pattern;
        }

        public string Pattern { get; }

        public Dictionary<string, string> Properties { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public void Set(string key, string value) => Properties[key] = value;
    }

    private static class EditorConfigSettingsParser
    {
        public static EditorConfigSettings Parse(
            IReadOnlyDictionary<string, string> values)
        {
            var style = Get(values, "indent_style");
            var inputMode = style switch
            {
                "space" => IndentationInputMode.Spaces,
                "tab" => IndentationInputMode.Tab,
                _ => (IndentationInputMode?)null
            };

            var indentSizeText = Get(values, "indent_size");
            var indentSize = ParsePositiveInt(indentSizeText);
            var tabWidth = ParsePositiveInt(Get(values, "tab_width"));
            var effectiveTabWidth = tabWidth ?? indentSize ?? 4;
            if (string.Equals(indentSizeText, "tab", StringComparison.OrdinalIgnoreCase))
            {
                indentSize = effectiveTabWidth;
            }

            if (inputMode == IndentationInputMode.Spaces && indentSize is null)
            {
                indentSize = 4;
            }

            var hasIndentationSetting = inputMode is not null
                || indentSize is not null
                || tabWidth is not null;

            return new EditorConfigSettings(
                inputMode,
                indentSize,
                hasIndentationSetting ? effectiveTabWidth : null,
                ParseLineEnding(Get(values, "end_of_line")),
                ParseEncoding(Get(values, "charset")),
                ParseBoolean(Get(values, "insert_final_newline")),
                ParseBoolean(Get(values, "trim_trailing_whitespace")));
        }

        private static string? Get(
            IReadOnlyDictionary<string, string> values,
            string key) => values.TryGetValue(key, out var value)
            ? value.Trim().ToLowerInvariant()
            : null;

        private static int? ParsePositiveInt(string? value) =>
            int.TryParse(
                value,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed) && parsed > 0
                ? parsed
                : null;

        private static bool? ParseBoolean(string? value) => value switch
        {
            "true" => true,
            "false" => false,
            _ => null
        };

        private static LineEndingKind? ParseLineEnding(string? value) => value switch
        {
            "lf" => LineEndingKind.Lf,
            "crlf" => LineEndingKind.CrLf,
            "cr" => LineEndingKind.Cr,
            _ => null
        };

        private static TextEncodingKind? ParseEncoding(string? value) => value switch
        {
            "utf-8" => TextEncodingKind.Utf8,
            "utf-8-bom" => TextEncodingKind.Utf8Bom,
            "utf-16le" => TextEncodingKind.Utf16LittleEndian,
            "utf-16be" => TextEncodingKind.Utf16BigEndian,
            "latin1" => TextEncodingKind.Latin1,
            _ => null
        };
    }

    private static class EditorConfigGlob
    {
        public static bool IsMatch(string pattern, string relativePath, string fullPath)
        {
            var normalizedPattern = NormalizePath(pattern.Trim());
            var candidate = normalizedPattern.Contains('/')
                ? relativePath
                : Path.GetFileName(fullPath);
            if (normalizedPattern.StartsWith('/'))
            {
                normalizedPattern = normalizedPattern[1..];
                candidate = relativePath;
            }

            try
            {
                var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
                matcher.AddInclude(normalizedPattern);
                return matcher.Match(candidate).HasMatches;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
