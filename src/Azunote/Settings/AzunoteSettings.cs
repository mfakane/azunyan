using System.Text;
using System.Text.Json.Serialization;
using Azunyan.Syntax;
using Tomlyn;
using Tomlyn.Serialization;

namespace Azunote;

public sealed class AzunoteSettings
{
    public ShellCommandSettings Terminal { get; set; } = new()
    {
        Command = "wt.exe",
        Arguments = ["-d", "${documentDir}"]
    };

    public ShellCommandSettings Explorer { get; set; } = new()
    {
        Command = "explorer.exe",
        Arguments = ["/select,\"${file}\""]
    };

    /// <summary>
    /// The tools discovered below the settings directory's tools folder.
    /// These values are derived from tool definition files and are not written
    /// into settings.toml.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<ExternalToolSettings> ExternalTools { get; internal set; } = [];

    /// <summary>
    /// Syntax language definitions discovered below the settings directory's
    /// modes folder. These values are derived from mode definition files.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<SyntaxLanguageDefinition> CustomSyntaxModes { get; internal set; } = [];

    /// <summary>
    /// The hierarchical menu nodes discovered below the tools folder.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<ExternalToolMenuNode> ExternalToolMenu { get; internal set; } = [];
}

[TomlSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[TomlSerializable(typeof(AzunoteSettings))]
[TomlSerializable(typeof(AzunoteState))]
[TomlSerializable(typeof(WindowLayoutState))]
[TomlSerializable(typeof(ShellCommandSettings))]
[TomlSerializable(typeof(ExternalToolSettings))]
[TomlSerializable(typeof(ExternalToolLaunchSettings))]
[TomlSerializable(typeof(ExternalToolWhenSettings))]
[TomlSerializable(typeof(CustomSyntaxModeSettings))]
[TomlSerializable(typeof(CustomSyntaxRuleSettings))]
internal sealed partial class AzunoteTomlSerializerContext : TomlSerializerContext
{
}

public sealed class ShellCommandSettings
{
    public string Command { get; set; } = string.Empty;

    [TomlPropertyName("args")]
    public string[] Arguments { get; set; } = [];

    public string? WorkingDirectory { get; set; }

    internal void Validate(string sectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        Arguments ??= [];
        if (string.IsNullOrWhiteSpace(Command))
        {
            throw new SettingsFileException($"The {sectionName} needs a command.");
        }
    }
}

public enum ExternalToolVisibility
{
    Always,
    WhenAvailable
}

public enum ExternalToolFileCondition
{
    Any,
    Backed,
    Untitled
}

public enum ExternalToolSelectionCondition
{
    Any,
    Empty,
    NonEmpty
}

public enum ExternalToolDocumentCondition
{
    Any,
    Clean,
    Dirty
}

internal static class ExternalToolEnumValues
{
    public static string ToTomlValue<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var name = value.ToString();
        return name.Length == 0
            ? name
            : char.ToLowerInvariant(name[0]) + name[1..];
    }

    public static TEnum Parse<TEnum>(string? value, string propertyName)
        where TEnum : struct, Enum
    {
        foreach (var enumValue in Enum.GetValues<TEnum>())
        {
            if (string.Equals(
                    ToTomlValue(enumValue),
                    value,
                    StringComparison.Ordinal))
            {
                return enumValue;
            }
        }

        throw new SettingsFileException(
            $"Invalid {propertyName} value '{value}'. Expected a camelCase {typeof(TEnum).Name} value.");
    }
}

public sealed class ExternalToolLaunchSettings
{
    public string Command { get; set; } = string.Empty;

    [TomlPropertyName("args")]
    public string[] Arguments { get; set; } = [];

    public string? WorkingDirectory { get; set; }

    public string Input { get; set; } = ExternalToolEnumValues.ToTomlValue(ExternalToolInputMode.None);

    public string Output { get; set; } = ExternalToolEnumValues.ToTomlValue(ExternalToolOutputMode.Ignore);
}

public sealed class ExternalToolWhenSettings
{
    public string[] Extensions { get; set; } = [];

    public string[] Patterns { get; set; } = [];

    public string[] Languages { get; set; } = [];

    public string File { get; set; } = ExternalToolEnumValues.ToTomlValue(ExternalToolFileCondition.Any);

    public string Selection { get; set; } = ExternalToolEnumValues.ToTomlValue(ExternalToolSelectionCondition.Any);

    public string Document { get; set; } = ExternalToolEnumValues.ToTomlValue(ExternalToolDocumentCondition.Any);

    public string[] Os { get; set; } = [];
}

public sealed class ExternalToolSettings
{
    public string Name { get; set; } = string.Empty;

    public string? Shortcut { get; set; }

    public string Visibility { get; set; } = ExternalToolEnumValues.ToTomlValue(ExternalToolVisibility.Always);

    public ExternalToolLaunchSettings Launch { get; set; } = new();

    public ExternalToolWhenSettings When { get; set; } = new();

    [TomlPropertyName("env")]
    public Dictionary<string, string> Environment { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The directory containing this tool definition. This is metadata from
    /// the discovery process, not a TOML property.
    /// </summary>
    [JsonIgnore]
    public string? DefinitionDirectory { get; internal set; }

    /// <summary>
    /// The source file for this tool definition. This is metadata from the
    /// discovery process, not a TOML property.
    /// </summary>
    [JsonIgnore]
    public string? DefinitionPath { get; internal set; }

    public ExternalToolDefinition ToDefinition()
    {
        Validate();

        return new ExternalToolDefinition(
            Launch.Command,
            Launch.Arguments,
            ExternalToolEnumValues.Parse<ExternalToolInputMode>(Launch.Input, "launch.input"),
            ExternalToolEnumValues.Parse<ExternalToolOutputMode>(Launch.Output, "launch.output"),
            Launch.WorkingDirectory,
            Environment,
            DefinitionDirectory);
    }

    internal void Validate()
    {
        Launch ??= new();
        Launch.Arguments ??= [];
        Environment ??= new(StringComparer.OrdinalIgnoreCase);
        When ??= new();
        When.Extensions ??= [];
        When.Patterns ??= [];
        When.Languages ??= [];
        When.Os ??= [];

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new SettingsFileException("Each external tool needs a non-empty name.");
        }

        if (string.IsNullOrWhiteSpace(Launch.Command))
        {
            throw new SettingsFileException($"External tool '{Name}' needs a command.");
        }

        _ = ExternalToolEnumValues.Parse<ExternalToolInputMode>(Launch.Input, "launch.input");
        _ = ExternalToolEnumValues.Parse<ExternalToolOutputMode>(Launch.Output, "launch.output");
        _ = ExternalToolEnumValues.Parse<ExternalToolVisibility>(Visibility, nameof(Visibility));
        _ = ExternalToolEnumValues.Parse<ExternalToolFileCondition>(When.File, "when.file");
        _ = ExternalToolEnumValues.Parse<ExternalToolSelectionCondition>(When.Selection, "when.selection");
        _ = ExternalToolEnumValues.Parse<ExternalToolDocumentCondition>(When.Document, "when.document");

        Shortcut = string.IsNullOrWhiteSpace(Shortcut) ? null : Shortcut.Trim();
        if (Shortcut is not null && !ExternalToolShortcut.TryParse(Shortcut, out _))
        {
            throw new SettingsFileException(
                $"Invalid shortcut '{Shortcut}' for external tool '{Name}'.");
        }
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

/// <summary>
/// A node in the External Tools menu. A node with a tool is a leaf; a node
/// without one is a folder whose children become a submenu.
/// </summary>
public sealed class ExternalToolMenuNode
{
    public ExternalToolMenuNode(
        string name,
        ExternalToolSettings? tool = null,
        IReadOnlyList<ExternalToolMenuNode>? children = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Tool = tool;
        Children = children ?? [];
    }

    public string Name { get; }

    public ExternalToolSettings? Tool { get; }

    public IReadOnlyList<ExternalToolMenuNode> Children { get; }

    public bool IsTool => Tool is not null;
}

public sealed class ExternalToolCatalog
{
    public ExternalToolCatalog(
        IReadOnlyList<ExternalToolMenuNode> menu,
        IReadOnlyList<ExternalToolSettings> tools)
    {
        Menu = menu;
        Tools = tools;
    }

    public IReadOnlyList<ExternalToolMenuNode> Menu { get; }

    public IReadOnlyList<ExternalToolSettings> Tools { get; }
}

public static class SettingsFileService
{
    public const string SettingsDirectoryName = "Azunote";
    public const string SettingsFileName = "settings.toml";
    public const string ToolsDirectoryName = "tools";
    public const string ModesDirectoryName = "modes";

    public static string GetDefaultDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }

        return Path.Combine(localAppData, SettingsDirectoryName);
    }

    public static string GetSettingsFilePath(string settingsDirectory) =>
        Path.Combine(GetFullDirectoryPath(settingsDirectory), SettingsFileName);

    public static string GetToolsDirectoryPath(string settingsDirectory) =>
        Path.Combine(GetFullDirectoryPath(settingsDirectory), ToolsDirectoryName);

    public static string GetModesDirectoryPath(string settingsDirectory) =>
        Path.Combine(GetFullDirectoryPath(settingsDirectory), ModesDirectoryName);

    public static async Task EnsureExistsAsync(
        string settingsDirectory,
        CancellationToken cancellationToken = default)
    {
        var directory = GetFullDirectoryPath(settingsDirectory);
        var toolsDirectory = Path.Combine(directory, ToolsDirectoryName);
        var toolsDirectoryExisted = Directory.Exists(toolsDirectory);
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(toolsDirectory);
        await EnsureDefaultModesAsync(directory, cancellationToken);
        if (!toolsDirectoryExisted)
        {
            await EnsureDefaultToolsAsync(directory, cancellationToken);
        }

        var settingsPath = Path.Combine(directory, SettingsFileName);
        if (!File.Exists(settingsPath))
        {
            await EnsureDefaultSettingsAsync(settingsPath, cancellationToken);
        }
    }

    public static async Task<AzunoteSettings> LoadAsync(
        string settingsDirectory,
        CancellationToken cancellationToken = default)
    {
        var directory = GetFullDirectoryPath(settingsDirectory);
        var settingsPath = Path.Combine(directory, SettingsFileName);
        var settings = File.Exists(settingsPath)
            ? await DeserializeAsync(
                settingsPath,
                AzunoteTomlSerializerContext.Default.AzunoteSettings,
                cancellationToken)
            : new AzunoteSettings();
        settings.Terminal ??= new();
        settings.Terminal.Validate("terminal");
        settings.Explorer ??= new();
        settings.Explorer.Validate("explorer");

        var catalog = await ExternalToolDiscovery.LoadAsync(
            Path.Combine(directory, ToolsDirectoryName),
            cancellationToken);
        settings.ExternalTools = catalog.Tools;
        settings.ExternalToolMenu = catalog.Menu;
        settings.CustomSyntaxModes = await CustomSyntaxModeDiscovery.LoadAsync(
            GetModesDirectoryPath(directory),
            cancellationToken);
        return settings;
    }

    public static async Task SaveAsync(
        string settingsDirectory,
        AzunoteSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = GetFullDirectoryPath(settingsDirectory);
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, ToolsDirectoryName));

        var toml = TomlSerializer.Serialize(
            settings,
            AzunoteTomlSerializerContext.Default.AzunoteSettings);
        await File.WriteAllTextAsync(
            Path.Combine(directory, SettingsFileName),
            "# Azunote settings. This file uses TOML syntax.\n" + toml,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
    }

    internal static async Task<T> DeserializeAsync<T>(
        string path,
        TomlTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            var text = await File.ReadAllTextAsync(path, cancellationToken);
            return TomlSerializer.Deserialize(text, typeInfo)
                ?? throw new SettingsFileException($"Could not parse TOML file '{path}'.");
        }
        catch (SettingsFileException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SettingsFileException(
                $"Could not parse TOML file '{path}': {exception.Message}",
                exception);
        }
    }

    private static string GetFullDirectoryPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    private static async Task EnsureDefaultModesAsync(
        string settingsDirectory,
        CancellationToken cancellationToken)
    {
        var modesDirectory = GetModesDirectoryPath(settingsDirectory);
        if (Directory.Exists(modesDirectory))
        {
            return;
        }

        var defaultModesDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Resources",
            "DefaultAppData",
            ModesDirectoryName);
        Directory.CreateDirectory(modesDirectory);
        if (!Directory.Exists(defaultModesDirectory))
        {
            return;
        }

        foreach (var sourcePath in Directory.EnumerateFiles(
                     defaultModesDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(defaultModesDirectory, sourcePath);
            var destinationPath = Path.Combine(modesDirectory, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            await using var source = File.OpenRead(sourcePath);
            await using var destination = File.Create(destinationPath);
            await source.CopyToAsync(destination, cancellationToken);
        }
    }

    private static async Task EnsureDefaultSettingsAsync(
        string settingsPath,
        CancellationToken cancellationToken)
    {
        var defaultSettingsPath = Path.Combine(
            AppContext.BaseDirectory,
            "Resources",
            "DefaultAppData",
            SettingsFileName);
        if (!File.Exists(defaultSettingsPath))
        {
            await File.WriteAllTextAsync(
                settingsPath,
                string.Empty,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            return;
        }

        await using var source = File.OpenRead(defaultSettingsPath);
        await using var destination = File.Create(settingsPath);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static async Task EnsureDefaultToolsAsync(
        string settingsDirectory,
        CancellationToken cancellationToken)
    {
        var defaultToolsDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Resources",
            "DefaultAppData",
            ToolsDirectoryName);
        var toolsDirectory = GetToolsDirectoryPath(settingsDirectory);
        if (!Directory.Exists(defaultToolsDirectory))
        {
            return;
        }

        foreach (var sourcePath in Directory.EnumerateFiles(
                     defaultToolsDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(defaultToolsDirectory, sourcePath);
            var destinationPath = Path.Combine(toolsDirectory, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            if (File.Exists(destinationPath))
            {
                continue;
            }

            await using var source = File.OpenRead(sourcePath);
            await using var destination = File.Create(destinationPath);
            await source.CopyToAsync(destination, cancellationToken);
        }
    }
}
