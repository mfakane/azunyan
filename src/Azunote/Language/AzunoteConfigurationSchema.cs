using Azunyan.Core;
using Azunyan.Syntax;
using System.Diagnostics.CodeAnalysis;

namespace Azunote;

[SuppressMessage(
    "Design",
    "CA1720",
    Justification = "These names intentionally match the configuration schema's value-kind terminology.")]
public enum AzunoteSchemaValueKind
{
    String,
    StringOrArray,
    Boolean,
    Integer,
    Array,
    Map,
    Enum
}

/// <summary>
/// NativeAOT-safe metadata used by Azunote's configuration completion
/// provider. It is deliberately independent from the TOML serialization DTOs.
/// </summary>
public sealed record AzunoteSchemaField(
    string Name,
    AzunoteSchemaValueKind ValueKind,
    string Classification = "property",
    IReadOnlyList<string>? AllowedValues = null,
    string? Documentation = null,
    bool SupportsPlaceholders = false)
{
    public IReadOnlyList<string> Values => AllowedValues ?? Array.Empty<string>();
}

public sealed record AzunoteSchemaTable(
    string Path,
    IReadOnlyList<AzunoteSchemaField> Fields,
    bool AllowsDynamicFields = false);

public sealed record AzunoteSchemaDefinition(
    string Id,
    IReadOnlyList<string> Patterns,
    IReadOnlyList<AzunoteSchemaTable> Tables);

/// <summary>
/// Compiled-in schemas for the TOML files Azunote owns. These descriptors are
/// explicit rather than reflection-generated so they work in NativeAOT builds.
/// </summary>
public static class AzunoteSchemaCatalog
{
    public static AzunoteSchemaDefinition Settings { get; } = new(
        "azunote.settings",
        ["settings.toml"],
        [
            new AzunoteSchemaTable(
                string.Empty,
                [
                    Field("fontFamily", AzunoteSchemaValueKind.String, documentation: "Font used by the editor and its renderer."),
                    Field("fontSize", AzunoteSchemaValueKind.Integer, documentation: "Font size used by the editor and its renderer.")
                ]),
            new AzunoteSchemaTable(
                "debug",
                [
                    Field(
                        "logging",
                        AzunoteSchemaValueKind.Array,
                        documentation: "Detailed operation logs: all, render, clipboard, key, or input. Empty disables them.")
                ]),
            new AzunoteSchemaTable(
                "terminal",
                [
                    Field("command", AzunoteSchemaValueKind.String, "command", supportsPlaceholders: true),
                    Field("args", AzunoteSchemaValueKind.Array, "property", supportsPlaceholders: true),
                    Field("workingDirectory", AzunoteSchemaValueKind.String, "path", supportsPlaceholders: true)
                ]),
            new AzunoteSchemaTable(
                "explorer",
                [
                    Field("command", AzunoteSchemaValueKind.String, "command", supportsPlaceholders: true),
                    Field("args", AzunoteSchemaValueKind.Array, "property", supportsPlaceholders: true),
                    Field("workingDirectory", AzunoteSchemaValueKind.String, "path", supportsPlaceholders: true)
                ])
        ]);

    public static AzunoteSchemaDefinition ExternalTool { get; } = new(
        "azunote.tool",
        ["*.tool.toml", "manifest.toml", "*/manifest.toml"],
        [
            new AzunoteSchemaTable(
                string.Empty,
                [
                    Field("name", AzunoteSchemaValueKind.String, "property", documentation: "The tool name shown in the Tools menu."),
                    Field("shortcut", AzunoteSchemaValueKind.String, "property", documentation: "Optional keyboard shortcut, such as Alt+Shift+F."),
                    Field("priority", AzunoteSchemaValueKind.Integer, documentation: "Shortcut tie-break. Higher wins among enabled tools on the same key. Defaults to 0."),
                    Field("menus", AzunoteSchemaValueKind.Array, documentation: "Menu targets: tools, context, or both. Defaults to tools."),
                    EnumField("visibility", "always", "whenAvailable")
                ]),
            new AzunoteSchemaTable(
                "launch",
                [
                    Field("command", AzunoteSchemaValueKind.String, "command", supportsPlaceholders: true),
                    Field("path", AzunoteSchemaValueKind.StringOrArray, "path", supportsPlaceholders: true, documentation: "Directories searched before PATH. A missing directory is skipped."),
                    Field("args", AzunoteSchemaValueKind.Array, "property", supportsPlaceholders: true),
                    Field("cmd", AzunoteSchemaValueKind.StringOrArray, "command", supportsPlaceholders: true),
                    Field("pwsh", AzunoteSchemaValueKind.StringOrArray, "command", supportsPlaceholders: true),
                    Field("workingDirectory", AzunoteSchemaValueKind.String, "path", supportsPlaceholders: true),
                    EnumField("input", "none", "filePath", "document", "selection"),
                    Field("per", AzunoteSchemaValueKind.String, documentation: "none, line, or regex:<pattern>; regex runs once per match"),
                    Field("stdin", AzunoteSchemaValueKind.String, supportsPlaceholders: true),
                    OutputActionField("output"),
                    OutputActionField("stdout"),
                    OutputActionField("stderr"),
                    new AzunoteSchemaField(
                        "stream",
                        AzunoteSchemaValueKind.Enum,
                        AllowedValues: ["output", "stdout", "stderr"],
                        Documentation: "Channels applied while the tool runs, as a name or an array.")
                ]),
            new AzunoteSchemaTable(
                "when",
                [
                    Field("extensions", AzunoteSchemaValueKind.Array, "property"),
                    Field("patterns", AzunoteSchemaValueKind.Array, "property"),
                    Field("languages", AzunoteSchemaValueKind.Array, "property"),
                    EnumField("file", "any", "backed", "untitled"),
                    EnumField("selection", "any", "empty", "nonEmpty"),
                    EnumField("document", "any", "clean", "dirty"),
                    Field("os", AzunoteSchemaValueKind.Array, "property"),
                    Field("exists", AzunoteSchemaValueKind.StringOrArray, documentation: "Path or glob that must exist. Entries are AND; | is OR; ! negates; ./ is the document directory only.")
                ]),
            new AzunoteSchemaTable(
                "env",
                [],
                AllowsDynamicFields: true)
        ]);

    public static AzunoteSchemaDefinition CustomMode { get; } = new(
        "azunote.mode",
        ["modes/*.toml"],
        [
            new AzunoteSchemaTable(
                string.Empty,
                [
                    Field("id", AzunoteSchemaValueKind.String),
                    Field("displayName", AzunoteSchemaValueKind.String),
                    Field("patterns", AzunoteSchemaValueKind.Array),
                    Field("completionTriggerCharacters", AzunoteSchemaValueKind.Array),
                    Field("rules", AzunoteSchemaValueKind.Array)
                ]),
            new AzunoteSchemaTable(
                "rules",
                [
                    EnumField("type", "delimited", "line", "literal", "keyword", "regex"),
                    Field("classification", AzunoteSchemaValueKind.String),
                    Field("open", AzunoteSchemaValueKind.String),
                    Field("close", AzunoteSchemaValueKind.String),
                    Field("token", AzunoteSchemaValueKind.String),
                    Field("pattern", AzunoteSchemaValueKind.String),
                    Field("escapePrefix", AzunoteSchemaValueKind.String),
                    Field("escapedEndToken", AzunoteSchemaValueKind.String),
                    Field("allowLineBreaks", AzunoteSchemaValueKind.Boolean),
                    Field("requireLineStart", AzunoteSchemaValueKind.Boolean),
                    Field("caseSensitive", AzunoteSchemaValueKind.Boolean),
                    Field("words", AzunoteSchemaValueKind.Array)
                ])
        ]);

    public static IReadOnlyList<AzunoteSchemaDefinition> All { get; } =
    [
        Settings,
        ExternalTool,
        CustomMode
    ];

    public static IReadOnlyList<string> Patterns { get; } = All
        .SelectMany(schema => schema.Patterns)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static IReadOnlyList<AzunoteSchemaDefinition> ForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return All;
        }

        var bestScore = -1;
        var matches = new List<AzunoteSchemaDefinition>();
        foreach (var schema in All)
        {
            var score = SyntaxLanguageDefinition.GetPatternMatchScore(path, schema.Patterns);
            if (score < 0)
            {
                continue;
            }

            if (score > bestScore)
            {
                bestScore = score;
                matches.Clear();
            }

            if (score == bestScore)
            {
                matches.Add(schema);
            }
        }

        return matches.Count == 0 ? All : matches;
    }

    private static AzunoteSchemaField Field(
        string name,
        AzunoteSchemaValueKind valueKind,
        string classification = "property",
        string? documentation = null,
        bool supportsPlaceholders = false) =>
        new(
            name,
            valueKind,
            classification,
            Documentation: documentation,
            SupportsPlaceholders: supportsPlaceholders);

    private static AzunoteSchemaField OutputActionField(string name) =>
        new(
            name,
            AzunoteSchemaValueKind.Enum,
            AllowedValues: ["ignore", "replaceDocument", "replaceSelection", "newDocument", "reloadFile", "showCompletion"],
            Documentation: "An action or [zero-action, non-zero-action].");

    private static AzunoteSchemaField EnumField(
        string name,
        params string[] values) =>
        new(name, AzunoteSchemaValueKind.Enum, AllowedValues: values);
}

/// <summary>Provides schema fields and enum values for the active TOML file.</summary>
public sealed class AzunoteConfigurationCompletionProvider : ICompletionProvider
{
    private readonly IReadOnlyList<AzunoteSchemaDefinition> _schemas;

    public AzunoteConfigurationCompletionProvider(
        IReadOnlyList<AzunoteSchemaDefinition>? schemas = null)
    {
        _schemas = schemas ?? AzunoteSchemaCatalog.All;
    }

    public ValueTask<CompletionResult?> GetCompletionsAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = context.Snapshot;
        var line = snapshot.Lines.GetLine(context.Position);
        var lineStart = snapshot.Lines.GetLineStart(line);
        var lineText = snapshot.GetText(snapshot.Lines.GetLineRange(line));
        var column = context.Position - lineStart;
        var beforeCaret = lineText[..Math.Min(column, lineText.Length)];
        var tablePath = FindTablePath(snapshot, line);

        if (TryGetPlaceholderContext(
                beforeCaret,
                tablePath,
                context.Position < snapshot.Length && snapshot.Text[context.Position] == '}',
                out var placeholderStart,
                out var placeholderPrefix,
                out var preserveClosingBrace))
        {
            var items = ExternalToolPlaceholderCompletionProvider.GetItems(
                placeholderPrefix,
                preserveClosingBrace,
                cancellationToken: cancellationToken);
            return ValueTask.FromResult<CompletionResult?>(
                new CompletionResult(
                    TextRange.FromBounds(
                        lineStart + placeholderStart + (preserveClosingBrace ? 2 : 0),
                        context.Position),
                    items));
        }

        if (TryGetValueContext(beforeCaret, out var valueStart, out var valuePrefix, out var key))
        {
            var field = FindFields(tablePath)
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, key, StringComparison.OrdinalIgnoreCase));
            if (field is null || field.Values.Count == 0)
            {
                return ValueTask.FromResult<CompletionResult?>(null);
            }

            var items = field.Values
                .Where(value => string.IsNullOrEmpty(valuePrefix)
                    || value.StartsWith(valuePrefix, StringComparison.OrdinalIgnoreCase))
                .Select(value => new CompletionItem(
                    FormatTomlValue(value),
                    FormatTomlValue(value),
                    field.ValueKind.ToString(),
                    field.Documentation))
                .ToArray();
            return ValueTask.FromResult<CompletionResult?>(
                new CompletionResult(
                    TextRange.FromBounds(lineStart + valueStart, context.Position),
                    items));
        }

        var keyStart = beforeCaret.Length;
        while (keyStart > 0 && IsKeyCharacter(beforeCaret[keyStart - 1]))
        {
            keyStart--;
        }

        var keyPrefix = beforeCaret[keyStart..];
        if (beforeCaret[..keyStart].TrimEnd().EndsWith('='))
        {
            return ValueTask.FromResult<CompletionResult?>(null);
        }

        var keyItems = FindFields(tablePath)
            .Where(field => field.Name.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(field => new CompletionItem(
                field.Name,
                field.Name,
                field.ValueKind.ToString(),
                field.Documentation))
            .ToArray();
        return ValueTask.FromResult<CompletionResult?>(
            new CompletionResult(
                TextRange.FromBounds(lineStart + keyStart, context.Position),
                keyItems));
    }

    private AzunoteSchemaField[] FindFields(string tablePath) =>
        _schemas
            .SelectMany(schema => schema.Tables)
            .Where(table => string.Equals(table.Path, tablePath, StringComparison.OrdinalIgnoreCase))
            .SelectMany(table => table.Fields)
            .GroupBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

    private bool TryGetPlaceholderContext(
        string text,
        string tablePath,
        bool hasClosingBrace,
        out int placeholderStart,
        out string placeholderPrefix,
        out bool preserveClosingBrace)
    {
        placeholderStart = 0;
        placeholderPrefix = string.Empty;
        preserveClosingBrace = false;

        if (!TryGetValueContext(text, out _, out _, out var key)
            || !SupportsPlaceholders(tablePath, key))
        {
            return false;
        }

        var opening = text.LastIndexOf("${", StringComparison.Ordinal);
        if (opening < 0)
        {
            return false;
        }

        var prefix = text[(opening + 2)..];
        if (prefix.Contains('{') || prefix.Contains('}'))
        {
            return false;
        }

        placeholderStart = opening;
        placeholderPrefix = prefix;
        preserveClosingBrace = hasClosingBrace;
        return true;
    }

    private bool SupportsPlaceholders(string tablePath, string key)
    {
        var table = _schemas
            .SelectMany(schema => schema.Tables)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Path, tablePath, StringComparison.OrdinalIgnoreCase));
        if (table is null)
        {
            return false;
        }

        if (table.AllowsDynamicFields)
        {
            return true;
        }

        return FindFields(tablePath)
            .FirstOrDefault(field =>
                string.Equals(field.Name, key, StringComparison.OrdinalIgnoreCase))
            ?.SupportsPlaceholders == true;
    }

    private static string FindTablePath(TextSnapshot snapshot, int currentLine)
    {
        var tablePath = string.Empty;
        for (var line = 0; line <= currentLine; line++)
        {
            var text = snapshot.GetText(snapshot.Lines.GetLineRange(line)).Trim();
            if (text.StartsWith("[[", StringComparison.Ordinal)
                && text.EndsWith("]]", StringComparison.Ordinal))
            {
                tablePath = text[2..^2].Trim();
            }
            else if (text.StartsWith('[')
                && text.EndsWith(']'))
            {
                tablePath = text[1..^1].Trim();
            }
        }

        return tablePath;
    }

    private static bool TryGetValueContext(
        string text,
        out int valueStart,
        out string valuePrefix,
        out string key)
    {
        valueStart = 0;
        valuePrefix = string.Empty;
        key = string.Empty;
        var equals = text.IndexOf('=');
        if (equals < 0)
        {
            return false;
        }

        var rawKey = text[..equals].Trim();
        if (rawKey.Length == 0 || rawKey.Any(character => !IsKeyCharacter(character)))
        {
            return false;
        }

        valueStart = equals + 1;
        while (valueStart < text.Length && char.IsWhiteSpace(text[valueStart]))
        {
            valueStart++;
        }

        key = rawKey;
        valuePrefix = text[valueStart..];
        if (valuePrefix.StartsWith('"') || valuePrefix.StartsWith('\''))
        {
            valuePrefix = valuePrefix[1..];
        }

        return true;
    }

    private static bool IsKeyCharacter(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '-' or '.';

    private static string FormatTomlValue(string value) => $"\"{value}\"";
}
