using System.Globalization;
using System.Text.Json.Nodes;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Azunote;

/// <summary>The TOML shape of a language mode's `[languageServer]` table.</summary>
public sealed class LanguageServerSettings
{
    public string? Command { get; set; }

    [TomlPropertyName("args")]
    public string[]? Arguments { get; set; }

    [TomlConverter(typeof(ExternalToolSearchPathTomlConverter))]
    public ExternalToolSearchPath? Path { get; set; }

    public string? LanguageId { get; set; }

    public TomlTable? InitializationOptions { get; set; }

    public TomlTable? Settings { get; set; }
}

/// <summary>
/// How to start the language server of a mode. <see cref="Command"/>,
/// <see cref="Arguments"/> and <see cref="SearchPath"/> are expanded for the
/// document when the server starts.
/// </summary>
public sealed record LanguageServerDefinition(
    string Command,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> SearchPath,
    string? LanguageId,
    JsonObject? InitializationOptions,
    JsonObject? Settings,
    string? DefinitionDirectory)
{
    internal static LanguageServerDefinition Create(
        LanguageServerSettings settings,
        string? definitionDirectory)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.Command))
        {
            throw new SettingsFileException("[languageServer] needs a command.");
        }

        return new LanguageServerDefinition(
            settings.Command.Trim(),
            settings.Arguments ?? [],
            settings.Path?.Values ?? [],
            string.IsNullOrWhiteSpace(settings.LanguageId) ? null : settings.LanguageId.Trim(),
            settings.InitializationOptions is null ? null : ToJson(settings.InitializationOptions),
            settings.Settings is null ? null : ToJson(settings.Settings),
            definitionDirectory);
    }

    /// <summary>The values of a TOML table as JSON, for the server.</summary>
    internal static JsonObject ToJson(TomlTable table)
    {
        var json = new JsonObject();
        foreach (var (key, value) in table)
        {
            json[key] = ToJsonValue(value);
        }

        return json;
    }

    private static JsonNode? ToJsonValue(object? value) => value switch
    {
        TomlTable table => ToJson(table),
        TomlTableArray tables => new JsonArray([.. tables.Select(table => (JsonNode?)ToJson(table))]),
        TomlArray array => new JsonArray([.. array.Select(ToJsonValue)]),
        string text => JsonValue.Create(text),
        bool flag => JsonValue.Create(flag),
        long number => JsonValue.Create(number),
        double number => JsonValue.Create(number),
        null => null,
        _ => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture))
    };

    /// <summary>
    /// Identifies the server process: documents whose servers share a key
    /// share one process in the same workspace folder.
    /// </summary>
    internal string ProcessKey =>
        string.Join('\0', [Command, .. Arguments, .. SearchPath, DefinitionDirectory ?? string.Empty]);
}
