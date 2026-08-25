using System.Text.RegularExpressions;
using Azunyan.Core;
using Azunyan.Syntax;

namespace Azunote;

/// <summary>The TOML shape accepted for one custom language mode.</summary>
public sealed class CustomSyntaxModeSettings
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string[] Extensions { get; set; } = [];

    public CustomSyntaxRuleSettings[] Rules { get; set; } = [];
}

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
    public static async Task<IReadOnlyList<SyntaxLanguageDefinition>> LoadAsync(
        string modesDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modesDirectory);
        if (!Directory.Exists(modesDirectory))
        {
            return [];
        }

        var modes = new List<SyntaxLanguageDefinition>();
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
                var definition = CreateDefinition(settings);
                if (!ids.Add(definition.Id))
                {
                    throw new SettingsFileException(
                        $"Duplicate syntax mode id '{definition.Id}' in '{path}'.");
                }

                modes.Add(definition);
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

    private static SyntaxLanguageDefinition CreateDefinition(CustomSyntaxModeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.Id))
        {
            throw new SettingsFileException("A syntax mode needs a non-empty id.");
        }

        if (string.IsNullOrWhiteSpace(settings.DisplayName))
        {
            throw new SettingsFileException(
                $"Syntax mode '{settings.Id}' needs a non-empty displayName.");
        }

        if (settings.Extensions is null)
        {
            settings.Extensions = [];
        }

        if (settings.Rules is null || settings.Rules.Length == 0)
        {
            throw new SettingsFileException(
                $"Syntax mode '{settings.Id}' needs at least one rule.");
        }

        var sources = settings.Rules.Select(CreateRule).ToArray();
        return new SyntaxLanguageDefinition(
            settings.Id,
            settings.DisplayName,
            settings.Extensions,
            sources);
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
