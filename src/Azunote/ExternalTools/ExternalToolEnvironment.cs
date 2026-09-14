using System.Text;

namespace Azunote;

internal sealed record ExternalToolEnvironmentSnapshot(
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyDictionary<string, string> Overrides);

internal static class ExternalToolEnvironmentResolver
{
    public static ExternalToolEnvironmentSnapshot Resolve(
        ExternalToolDefinition definition,
        ExternalToolContext context)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(context);

        var values = LoadProcessEnvironment();
        var dotenvValues = DotEnvFileLoader.Load(context.DocumentDirname);
        foreach (var environmentVariable in dotenvValues)
        {
            values[environmentVariable.Key] = environmentVariable.Value;
        }

        var baseValues = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        var configuredValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var environmentVariable in definition.Environment)
        {
            configuredValues[environmentVariable.Key] = context.Expand(
                environmentVariable.Value,
                baseValues);
        }

        var overrides = new Dictionary<string, string>(dotenvValues, StringComparer.OrdinalIgnoreCase);
        foreach (var environmentVariable in configuredValues)
        {
            values[environmentVariable.Key] = environmentVariable.Value;
            overrides[environmentVariable.Key] = environmentVariable.Value;
        }

        return new ExternalToolEnvironmentSnapshot(values, overrides);
    }

    private static Dictionary<string, string> LoadProcessEnvironment()
    {
        using var measurement = ShellPerformance.Measure("environment.read");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry environmentVariable in
                 Environment.GetEnvironmentVariables())
        {
            if (environmentVariable.Key is string key)
            {
                values[key] = environmentVariable.Value?.ToString() ?? string.Empty;
            }
        }

        return values;
    }
}

internal static class DotEnvFileLoader
{
    private const string DotEnvFileName = ".env";
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static IReadOnlyDictionary<string, string> Load(string? documentDirectory)
    {
        using var measurement = ShellPerformance.Measure("dotenv.search");
        var path = FindNearestFile(documentDirectory);
        if (path is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return Parse(File.ReadAllLines(path, Utf8));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or DecoderFallbackException
                or ArgumentException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in lines)
        {
            if (!TryParseLine(rawLine, out var key, out var value))
            {
                continue;
            }

            values[key] = value;
        }

        return values;
    }

    private static bool TryParseLine(
        string rawLine,
        out string key,
        out string value)
    {
        key = string.Empty;
        value = string.Empty;

        var line = rawLine.TrimStart('\uFEFF').Trim();
        if (line.Length == 0 || line.StartsWith('#'))
        {
            return false;
        }

        if (line.StartsWith("export", StringComparison.Ordinal)
            && line.Length > "export".Length
            && char.IsWhiteSpace(line["export".Length]))
        {
            line = line["export".Length..].TrimStart();
        }

        var separator = line.IndexOf('=');
        if (separator <= 0)
        {
            return false;
        }

        key = line[..separator].Trim();
        if (!IsValidKey(key))
        {
            key = string.Empty;
            return false;
        }

        return TryParseValue(line[(separator + 1)..].Trim(), out value);
    }

    private static bool TryParseValue(string text, out string value)
    {
        value = string.Empty;
        if (text.Length == 0)
        {
            return true;
        }

        if (text[0] is '\'' or '"')
        {
            var quote = text[0];
            var closingQuote = text.IndexOf(quote, 1);
            if (closingQuote < 0)
            {
                return false;
            }

            var suffix = text[(closingQuote + 1)..].Trim();
            if (suffix.Length > 0 && !suffix.StartsWith('#'))
            {
                return false;
            }

            value = text[1..closingQuote];
            return true;
        }

        if (text[0] == '#')
        {
            return true;
        }

        var comment = text.IndexOf(" #", StringComparison.Ordinal);
        value = (comment < 0 ? text : text[..comment]).TrimEnd();
        return true;
    }

    private static bool IsValidKey(string key)
    {
        if (key.Length == 0
            || !(char.IsLetter(key[0]) || key[0] == '_'))
        {
            return false;
        }

        return key.Skip(1).All(character =>
            char.IsLetterOrDigit(character) || character == '_');
    }

    private static string? FindNearestFile(string? documentDirectory)
    {
        for (var current = documentDirectory;
             !string.IsNullOrWhiteSpace(current);
             current = GetParentDirectory(current))
        {
            var path = Path.Combine(current, DotEnvFileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string? GetParentDirectory(string directory)
    {
        var parent = Directory.GetParent(directory)?.FullName;
        return string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase)
            ? null
            : parent;
    }
}
