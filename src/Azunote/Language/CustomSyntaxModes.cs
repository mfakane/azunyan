using System.Text.RegularExpressions;
using Azunyan.Core;
using Azunyan.Syntax;

namespace Azunote;

/// <summary>
/// The TOML shape accepted for one custom language mode, or for the changes to
/// a built-in one. A setting left out is null, so a change to a built-in mode
/// keeps the built-in value.
/// </summary>
public sealed class CustomSyntaxModeSettings
{
    public string? Id { get; set; }

    public string? DisplayName { get; set; }

    public string[]? Patterns { get; set; }

    /// <summary>Compatibility alias for definitions written before patterns.</summary>
    public string[]? Extensions { get; set; }

    /// <summary>Literal strings which trigger completion requests in this mode.</summary>
    public string[]? CompletionTriggerCharacters { get; set; }

    /// <summary>Glob patterns that mark a workspace folder besides the defaults.</summary>
    public string[]? WorkspacePatterns { get; set; }

    public CustomSyntaxRuleSettings[]? Rules { get; set; }

    public LanguageServerSettings? LanguageServer { get; set; }
}

/// <summary>
/// A language mode read from the modes folder. For a built-in id it holds only
/// the settings the file changes; otherwise <see cref="Syntax"/> is the new
/// mode and every other setting has a value.
/// </summary>
public sealed record CustomLanguageMode(
    string Id,
    string DefinitionPath,
    SyntaxLanguageDefinition? Syntax,
    string? DisplayName,
    IReadOnlyList<string>? Patterns,
    IReadOnlyList<string>? CompletionTriggerCharacters,
    IReadOnlyList<string>? WorkspacePatterns,
    LanguageServerDefinition? LanguageServer);

/// <summary>The TOML shape accepted for one ordered syntax rule.</summary>
public sealed class CustomSyntaxRuleSettings
{
    public string Type { get; set; } = string.Empty;

    public string Classification { get; set; } = string.Empty;

    public string? Open { get; set; }

    public string? Close { get; set; }

    public string? Token { get; set; }

    public string? Pattern { get; set; }

    public string? EscapePrefix { get; set; }

    public string? EscapedEndToken { get; set; }

    public bool AllowLineBreaks { get; set; } = true;

    public bool RequireLineStart { get; set; }

    public bool CaseSensitive { get; set; } = true;

    public string[] Words { get; set; } = [];
}

public static class CustomSyntaxModeDiscovery
{
    public static async Task<IReadOnlyList<CustomLanguageMode>> LoadAsync(
        string modesDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modesDirectory);
        if (!Directory.Exists(modesDirectory))
        {
            return [];
        }

        var modes = new List<CustomLanguageMode>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(modesDirectory, "*.toml", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CustomSyntaxModeSettings settings;
            try
            {
                settings = await SettingsFileService.DeserializeAsync(
                    path,
                    AzunoteTomlSerializerContext.Default.CustomSyntaxModeSettings,
                    cancellationToken);
            }
            catch (SettingsFileException exception)
            {
                throw new SettingsFileException(
                    $"Could not load syntax mode definition '{path}': {exception.Message}",
                    exception);
            }

            try
            {
                var mode = CreateMode(settings, path);
                if (!ids.Add(mode.Id))
                {
                    throw new SettingsFileException(
                        $"Duplicate syntax mode id '{mode.Id}' in '{path}'.");
                }

                modes.Add(mode);
            }
            catch (SettingsFileException exception)
            {
                throw new SettingsFileException(
                    $"Could not load syntax mode definition '{path}': {exception.Message}",
                    exception);
            }
            catch (Exception exception)
            {
                throw new SettingsFileException(
                    $"Could not load syntax mode definition '{path}': {exception.Message}",
                    exception);
            }
        }

        return modes;
    }

    private static CustomLanguageMode CreateMode(
        CustomSyntaxModeSettings settings,
        string definitionPath)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var id = string.IsNullOrWhiteSpace(settings.Id)
            ? Path.GetFileNameWithoutExtension(definitionPath)
            : settings.Id.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new SettingsFileException("A syntax mode needs a non-empty id.");
        }

        var displayName = string.IsNullOrWhiteSpace(settings.DisplayName)
            ? null
            : settings.DisplayName;
        var patterns = settings.Patterns
            ?? (settings.Extensions is { Length: > 0 } ? settings.Extensions : null);
        var languageServer = settings.LanguageServer is null
            ? null
            : LanguageServerDefinition.Create(
                settings.LanguageServer,
                Path.GetDirectoryName(Path.GetFullPath(definitionPath)));
        if (LanguageModeCatalog.IsBuiltIn(id))
        {
            if (settings.Rules is { Length: > 0 })
            {
                throw new SettingsFileException(
                    $"The built-in mode '{id}' keeps its own highlighting and cannot have rules.");
            }

            return new CustomLanguageMode(
                id,
                Path.GetFullPath(definitionPath),
                null,
                displayName,
                patterns is null ? null : SyntaxLanguageDefinition.NormalizePatterns(patterns),
                settings.CompletionTriggerCharacters,
                settings.WorkspacePatterns,
                languageServer);
        }

        if (settings.Rules is null || settings.Rules.Length == 0)
        {
            throw new SettingsFileException(
                $"Syntax mode '{id}' needs at least one rule.");
        }

        var syntax = new SyntaxLanguageDefinition(
            id,
            displayName ?? id,
            patterns ?? [],
            settings.Rules.Select(CreateRule).ToArray(),
            settings.CompletionTriggerCharacters,
            definitionPath);
        return new CustomLanguageMode(
            id,
            syntax.DefinitionPath!,
            syntax,
            syntax.DisplayName,
            syntax.Patterns,
            syntax.CompletionTriggerCharacters,
            settings.WorkspacePatterns ?? [],
            languageServer);
    }

    private static ISyntaxProvider CreateRule(CustomSyntaxRuleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.Type))
        {
            throw new SettingsFileException("Every syntax rule needs a type.");
        }

        if (string.IsNullOrWhiteSpace(settings.Classification))
        {
            throw new SettingsFileException(
                $"Syntax rule '{settings.Type}' needs a classification.");
        }

        var comparison = settings.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        return settings.Type.Trim().ToLowerInvariant() switch
        {
            "delimited" => new DelimitedSyntaxRule(
                Require(settings.Open, "open", settings.Type),
                Require(settings.Close, "close", settings.Type),
                settings.Classification,
                settings.AllowLineBreaks,
                settings.EscapePrefix,
                settings.EscapedEndToken,
                comparison),
            "line" or "lineremainder" => new LineRemainderSyntaxRule(
                Require(settings.Token, "token", settings.Type),
                settings.Classification,
                settings.RequireLineStart,
                comparison),
            "literal" => new LiteralSyntaxRule(
                Require(settings.Token, "token", settings.Type),
                settings.Classification,
                comparison),
            "keywords" or "keyword" => new KeywordSyntaxRule(
                settings.Words ?? [],
                settings.Classification,
                settings.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase),
            "regex" => new RegexSyntaxRule(
                Require(settings.Pattern, "pattern", settings.Type),
                settings.Classification,
                settings.CaseSensitive
                    ? RegexOptions.CultureInvariant
                    : RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
            _ => throw new SettingsFileException(
                $"Unknown syntax rule type '{settings.Type}'.")
        };
    }

    private static string Require(string? value, string name, string type)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new SettingsFileException(
                $"Syntax rule '{type}' needs a non-empty {name}.");
        }

        return value;
    }
}
