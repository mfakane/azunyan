using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tomlyn;

namespace Azunote;

public sealed class AzunoteSettings
{
    /// <summary>
    /// The tools discovered below the settings directory's tools folder.
    /// These values are derived from tool definition files and are not written
    /// into settings.toml.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<ExternalToolSettings> ExternalTools { get; internal set; } = [];

    /// <summary>
    /// The hierarchical menu nodes discovered below the tools folder.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<ExternalToolMenuNode> ExternalToolMenu { get; internal set; } = [];
}

public sealed class ExternalToolSettings
{
    public string Name { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public string[] Arguments { get; set; } = [];

    public string Input { get; set; } = nameof(ExternalToolInputMode.None);

    public string Output { get; set; } = nameof(ExternalToolOutputMode.Ignore);

    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// The directory containing this tool definition. This is metadata from
    /// the discovery process, not a TOML property.
    /// </summary>
    [JsonIgnore]
    public string? DefinitionDirectory { get; internal set; }

    public ExternalToolDefinition ToDefinition()
    {
        Validate();

        var workingDirectory = WorkingDirectory;
        if (!string.IsNullOrWhiteSpace(workingDirectory)
            && !Path.IsPathRooted(workingDirectory)
            && !string.IsNullOrWhiteSpace(DefinitionDirectory))
        {
            workingDirectory = Path.GetFullPath(Path.Combine(DefinitionDirectory, workingDirectory));
        }
        else if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            workingDirectory = DefinitionDirectory;
        }

        return new ExternalToolDefinition(
            Command,
            Arguments,
            ParseEnum<ExternalToolInputMode>(Input, nameof(Input)),
            ParseEnum<ExternalToolOutputMode>(Output, nameof(Output)),
            workingDirectory);
    }

    internal void Validate()
    {
        Arguments ??= [];

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

    private const string DefaultSettingsText = """
        # Azunote settings. This file uses TOML syntax.
        # External tools are defined below the tools/ directory.
        """ + "\n";

    private static readonly TomlSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

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

    public static async Task EnsureExistsAsync(
        string settingsDirectory,
        CancellationToken cancellationToken = default)
    {
        var directory = GetFullDirectoryPath(settingsDirectory);
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, ToolsDirectoryName));

        var settingsPath = Path.Combine(directory, SettingsFileName);
        if (!File.Exists(settingsPath))
        {
            await File.WriteAllTextAsync(
                settingsPath,
                DefaultSettingsText,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
        }
    }

    public static async Task<AzunoteSettings> LoadAsync(
        string settingsDirectory,
        CancellationToken cancellationToken = default)
    {
        var directory = GetFullDirectoryPath(settingsDirectory);
        var settingsPath = Path.Combine(directory, SettingsFileName);
        var settings = File.Exists(settingsPath)
            ? await DeserializeAsync<AzunoteSettings>(settingsPath, cancellationToken)
            : new AzunoteSettings();

        var catalog = await ExternalToolDiscovery.LoadAsync(
            Path.Combine(directory, ToolsDirectoryName),
            cancellationToken);
        settings.ExternalTools = catalog.Tools;
        settings.ExternalToolMenu = catalog.Menu;
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

        var toml = TomlSerializer.Serialize(settings, SerializerOptions);
        await File.WriteAllTextAsync(
            Path.Combine(directory, SettingsFileName),
            "# Azunote settings. This file uses TOML syntax.\n" + toml,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
    }

    internal static async Task<T> DeserializeAsync<T>(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var text = await File.ReadAllTextAsync(path, cancellationToken);
            return TomlSerializer.Deserialize<T>(text, SerializerOptions)
                ?? throw new SettingsFileException($"Could not parse TOML file '{path}'.");
        }
        catch (SettingsFileException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SettingsFileException($"Could not parse TOML file '{path}'.", exception);
        }
    }

    private static string GetFullDirectoryPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }
}
