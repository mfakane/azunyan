using System.Text;
using System.Text.Json.Serialization;
using Azunyan.Core;
using Azunyan.Syntax;
using Tomlyn;
using Tomlyn.Serialization;

namespace Azunote;

public sealed class AzunoteSettings
{
    public const string DefaultFontFamily = "Consolas";
    public const double DefaultFontSize = 14;

    public string FontFamily { get; set; } = DefaultFontFamily;

    public double FontSize { get; set; } = DefaultFontSize;

    public AzunoteDebugSettings Debug { get; set; } = new();

    public AzunoteToolsSettings Tools { get; set; } = new();

    public ShellCommandSettings Terminal { get; set; } = new()
    {
        Command = "wt.exe",
        Arguments = ["-d", "${documentDirname}"]
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

/// <summary>
/// Settings that apply to external tools as a whole. Individual tools are
/// defined by their own files below the tools folder.
/// </summary>
public sealed class AzunoteToolsSettings
{
    public const int DefaultPowerShellWarmIdleProcesses = 1;
    public const int MaximumPowerShellWarmProcesses = 16;

    /// <summary>
    /// Half the logical processor count, between 1 and 4. The processes a run
    /// reserves are started while that run's earlier parts are executing, so
    /// the useful depth depends on how many cores are free: measurements show
    /// a depth of 4 helping on 8 and 32 logical processors but costing time on
    /// 4. See tools/Azunote.Performance/README.md.
    /// </summary>
    public static readonly int DefaultPowerShellWarmProcesses =
        Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    /// <summary>
    /// Most PowerShell processes kept started and waiting at once. A run that
    /// is known to launch several processes, such as one using `per`, raises
    /// the count up to this limit for the duration of that run. The default is
    /// derived from the logical processor count; see
    /// <see cref="DefaultPowerShellWarmProcesses"/>.
    /// </summary>
    public int PowerShellWarmProcesses { get; set; } = DefaultPowerShellWarmProcesses;

    /// <summary>
    /// PowerShell processes kept waiting when no run is in progress. It is
    /// clamped to <see cref="PowerShellWarmProcesses"/>. Zero keeps no process
    /// waiting until a run raises the count.
    /// </summary>
    public int PowerShellWarmIdleProcesses { get; set; } = DefaultPowerShellWarmIdleProcesses;

    internal void Validate()
    {
        PowerShellWarmProcesses = Require(
            PowerShellWarmProcesses,
            nameof(PowerShellWarmProcesses),
            "tools.powerShellWarmProcesses");
        PowerShellWarmIdleProcesses = Require(
            PowerShellWarmIdleProcesses,
            nameof(PowerShellWarmIdleProcesses),
            "tools.powerShellWarmIdleProcesses");
        if (PowerShellWarmIdleProcesses > PowerShellWarmProcesses)
        {
            throw new SettingsFileException(
                "tools.powerShellWarmIdleProcesses must not be greater than "
                + $"tools.powerShellWarmProcesses ({PowerShellWarmProcesses}).");
        }
    }

    private static int Require(int value, string propertyName, string settingName)
    {
        var minimum = propertyName == nameof(PowerShellWarmProcesses) ? 1 : 0;
        if (value < minimum || value > MaximumPowerShellWarmProcesses)
        {
            throw new SettingsFileException(
                $"Invalid {settingName} value '{value}'. Expected {minimum} to "
                + $"{MaximumPowerShellWarmProcesses}.");
        }

        return value;
    }
}

public sealed class AzunoteDebugSettings
{
    /// <summary>
    /// Detailed editor operation-log categories. An empty list keeps the
    /// high-volume diagnostic log disabled.
    /// </summary>
    public string[] Logging { get; set; } = [];

    internal void Validate()
    {
        Logging ??= [];
        var categories = AzunyanDiagnosticCategory.None;
        foreach (var value in Logging)
        {
            if (!AzunyanDiagnosticCategories.TryParse(value, out var category))
            {
                var allowed = string.Join(
                    ", ",
                    new[] { AzunyanDiagnosticCategories.AllName }
                        .Concat(AzunyanDiagnosticCategories.Names));
                throw new SettingsFileException(
                    $"Invalid debug.logging category '{value}'. Expected one of: {allowed}.");
            }

            categories |= category;
        }

        Logging = AzunyanDiagnosticCategories.ToNames(categories).ToArray();
    }
}

[TomlSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    Converters = [
        typeof(ExternalToolOutputActionsTomlConverter),
        typeof(ExternalToolStreamChannelsTomlConverter),
        typeof(ExternalToolCommandTomlConverter)])]
[TomlSerializable(typeof(AzunoteSettings))]
[TomlSerializable(typeof(AzunoteDebugSettings))]
[TomlSerializable(typeof(AzunoteToolsSettings))]
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

public sealed class ExternalToolOutputActionsTomlConverter
    : TomlConverter<ExternalToolOutputActions>
{
    public override ExternalToolOutputActions Read(TomlReader reader)
    {
        if (reader.TokenType == TomlTokenType.String)
        {
            return Parse([reader.GetString()], reader);
        }

        if (reader.TokenType != TomlTokenType.StartArray)
        {
            throw reader.CreateException(
                "Expected an external-tool output action or a two-element action array.");
        }

        var values = new List<string>();
        while (reader.Read() && reader.TokenType != TomlTokenType.EndArray)
        {
            if (reader.TokenType != TomlTokenType.String)
            {
                throw reader.CreateException(
                    "External-tool output action arrays may contain only strings.");
            }

            values.Add(reader.GetString());
        }

        if (reader.TokenType != TomlTokenType.EndArray)
        {
            throw reader.CreateException("The external-tool output action array was not closed.");
        }

        var result = Parse(values, reader);
        reader.Read();
        return result;
    }

    public override void Write(
        TomlWriter writer,
        ExternalToolOutputActions value)
    {
        writer.WriteStartArray();
        writer.WriteStringValue(ExternalToolEnumValues.ToTomlValue(value.OnSuccess));
        writer.WriteStringValue(ExternalToolEnumValues.ToTomlValue(value.OnFailure));
        writer.WriteEndArray();
    }

    private static ExternalToolOutputActions Parse(
        List<string> values,
        TomlReader reader)
    {
        if (values.Count is not (1 or 2))
        {
            throw reader.CreateException(
                "External-tool output actions must contain one or two values.");
        }

        try
        {
            var success = ExternalToolEnumValues.Parse<ExternalToolOutputMode>(
                values[0],
                "external-tool output action");
            var failure = values.Count == 1
                ? success
                : ExternalToolEnumValues.Parse<ExternalToolOutputMode>(
                    values[1],
                    "external-tool output action");
            return new ExternalToolOutputActions(success, failure);
        }
        catch (SettingsFileException exception)
        {
            throw reader.CreateException(exception.Message);
        }
    }
}

public sealed class ExternalToolStreamChannelsTomlConverter
    : TomlConverter<ExternalToolStreamChannels>
{
    public override ExternalToolStreamChannels Read(TomlReader reader)
    {
        if (reader.TokenType == TomlTokenType.String)
        {
            return Parse([reader.GetString()], reader);
        }

        if (reader.TokenType != TomlTokenType.StartArray)
        {
            throw reader.CreateException(
                "Expected an external-tool stream channel or an array of them.");
        }

        var values = new List<string>();
        while (reader.Read() && reader.TokenType != TomlTokenType.EndArray)
        {
            if (reader.TokenType != TomlTokenType.String)
            {
                throw reader.CreateException(
                    "External-tool stream channel arrays may contain only strings.");
            }

            values.Add(reader.GetString());
        }

        if (reader.TokenType != TomlTokenType.EndArray)
        {
            throw reader.CreateException("The external-tool stream channel array was not closed.");
        }

        var result = Parse(values, reader);
        reader.Read();
        return result;
    }

    public override void Write(
        TomlWriter writer,
        ExternalToolStreamChannels value)
    {
        writer.WriteStartArray();
        foreach (var channel in new[]
                 {
                     ExternalToolOutputChannel.Mixed,
                     ExternalToolOutputChannel.Stdout,
                     ExternalToolOutputChannel.Stderr
                 })
        {
            if (ExternalToolStreaming.Streams(value, channel))
            {
                writer.WriteStringValue(ExternalToolStreaming.ToTomlValue(channel));
            }
        }

        writer.WriteEndArray();
    }

    private static ExternalToolStreamChannels Parse(
        List<string> values,
        TomlReader reader)
    {
        var channels = ExternalToolStreamChannels.None;
        foreach (var value in values)
        {
            if (!ExternalToolStreaming.TryParseChannel(value, out var channel))
            {
                throw reader.CreateException(
                    $"Invalid stream channel '{value}'. Expected output, stdout, or stderr.");
            }

            channels |= ExternalToolStreaming.ToFlag(channel);
        }

        return channels;
    }
}

public sealed record ExternalToolCommand
{
    public ExternalToolCommand(string value) =>
        Value = value ?? throw new ArgumentNullException(nameof(value));

    public ExternalToolCommand(IReadOnlyList<string> values)
        : this(Join(values))
    {
    }

    public string Value { get; }

    public static implicit operator ExternalToolCommand(string value) =>
        new(value);

    public static implicit operator ExternalToolCommand(string[] values) =>
        new ExternalToolCommand((IReadOnlyList<string>)values);

    private static string Join(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return string.Join(
            " ",
            values.Select(value =>
                value.Length == 0 || value.Any(char.IsWhiteSpace)
                    ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
                    : value));
    }
}

public sealed class ExternalToolCommandTomlConverter : TomlConverter<ExternalToolCommand>
{
    public override ExternalToolCommand Read(TomlReader reader)
    {
        if (reader.TokenType == TomlTokenType.String)
        {
            return new ExternalToolCommand(reader.GetString());
        }

        if (reader.TokenType != TomlTokenType.StartArray)
        {
            throw reader.CreateException(
                "Expected an external-tool shell command string or string array.");
        }

        var values = new List<string>();
        while (reader.Read() && reader.TokenType != TomlTokenType.EndArray)
        {
            if (reader.TokenType != TomlTokenType.String)
            {
                throw reader.CreateException(
                    "External-tool shell command arrays may contain only strings.");
            }

            values.Add(reader.GetString());
        }

        if (reader.TokenType != TomlTokenType.EndArray)
        {
            throw reader.CreateException(
                "The external-tool shell command array was not closed.");
        }

        reader.Read();
        return new ExternalToolCommand(values);
    }

    public override void Write(TomlWriter writer, ExternalToolCommand value) =>
        writer.WriteStringValue(value.Value);
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

public enum ExternalToolMenuTarget
{
    Tools,
    Context
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
    public string? Command { get; set; }

    [TomlConverter(typeof(ExternalToolCommandTomlConverter))]
    public ExternalToolCommand? Cmd { get; set; }

    [TomlConverter(typeof(ExternalToolCommandTomlConverter))]
    public ExternalToolCommand? Pwsh { get; set; }

    [TomlPropertyName("args")]
    public string[]? Arguments { get; set; } = [];

    public string? WorkingDirectory { get; set; }

    public string Input { get; set; } = ExternalToolEnumValues.ToTomlValue(ExternalToolInputMode.None);

    public string Per { get; set; } = "none";

    public string Stdin { get; set; } = string.Empty;

    [TomlConverter(typeof(ExternalToolOutputActionsTomlConverter))]
    public ExternalToolOutputActions Output { get; set; } = ExternalToolOutputActions.Ignore;

    [TomlConverter(typeof(ExternalToolOutputActionsTomlConverter))]
    public ExternalToolOutputActions Stdout { get; set; } = ExternalToolOutputActions.Ignore;

    [TomlConverter(typeof(ExternalToolOutputActionsTomlConverter))]
    public ExternalToolOutputActions Stderr { get; set; } = ExternalToolOutputActions.Ignore;

    [TomlConverter(typeof(ExternalToolStreamChannelsTomlConverter))]
    public ExternalToolStreamChannels Stream { get; set; } = ExternalToolStreamChannels.None;
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

    public string[] Menus { get; set; } =
        [ExternalToolEnumValues.ToTomlValue(ExternalToolMenuTarget.Tools)];

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

        var commandMode = HasCommand(Launch.Cmd)
            ? ExternalToolCommandMode.Cmd
            : HasCommand(Launch.Pwsh)
                ? ExternalToolCommandMode.Pwsh
                : ExternalToolCommandMode.Executable;
        var command = commandMode switch
        {
            ExternalToolCommandMode.Cmd => Launch.Cmd!.Value,
            ExternalToolCommandMode.Pwsh => Launch.Pwsh!.Value,
            _ => Launch.Command!
        };

        return new ExternalToolDefinition(
            command,
            Launch.Arguments ?? [],
            ExternalToolEnumValues.Parse<ExternalToolInputMode>(Launch.Input, "launch.input"),
            Launch.Per,
            Launch.Stdin,
            Launch.Output,
            Launch.Stdout,
            Launch.Stderr,
            Launch.WorkingDirectory,
            Environment,
            DefinitionDirectory,
            commandMode,
            Launch.Stream);
    }

    internal void Validate()
    {
        Menus ??= [ExternalToolEnumValues.ToTomlValue(ExternalToolMenuTarget.Tools)];
        Launch ??= new();
        Launch.Arguments ??= [];
        Launch.Stdin ??= string.Empty;
        Launch.Output ??= ExternalToolOutputActions.Ignore;
        Launch.Stdout ??= ExternalToolOutputActions.Ignore;
        Launch.Stderr ??= ExternalToolOutputActions.Ignore;
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

        Menus = Menus
            .Select(menu => ExternalToolEnumValues.Parse<ExternalToolMenuTarget>(menu, "menus"))
            .Select(ExternalToolEnumValues.ToTomlValue)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var commandModes = new[]
        {
            !string.IsNullOrWhiteSpace(Launch.Command),
            HasCommand(Launch.Cmd),
            HasCommand(Launch.Pwsh)
        };
        if (!commandModes.Any(mode => mode))
        {
            throw new SettingsFileException(
                $"External tool '{Name}' needs command, cmd, or pwsh.");
        }

        if (commandModes.Count(mode => mode) > 1)
        {
            throw new SettingsFileException(
                $"External tool '{Name}' must specify only one of command, cmd, or pwsh.");
        }

        if ((HasCommand(Launch.Cmd) || HasCommand(Launch.Pwsh))
            && Launch.Arguments is { Length: > 0 })
        {
            throw new SettingsFileException(
                $"External tool '{Name}' cannot specify args with cmd or pwsh.");
        }

        if (ExternalToolStreaming.Describe(
                Launch.Stream,
                Launch.Output,
                Launch.Stdout,
                Launch.Stderr) is { } streamError)
        {
            throw new SettingsFileException(
                $"External tool '{Name}': {streamError}");
        }

        _ = ExternalToolEnumValues.Parse<ExternalToolInputMode>(Launch.Input, "launch.input");
        try
        {
            _ = ExternalToolPer.Parse(Launch.Per, "launch.per");
        }
        catch (ArgumentException exception)
        {
            throw new SettingsFileException(exception.Message, exception);
        }
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

    private static bool HasCommand(ExternalToolCommand? command) =>
        command is not null && !string.IsNullOrWhiteSpace(command.Value);

    internal bool IsShownIn(ExternalToolMenuTarget target) =>
        Menus.Contains(ExternalToolEnumValues.ToTomlValue(target), StringComparer.Ordinal);

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
    public const string PortableDataDirectoryName = "appdata";
    public const string SettingsFileName = "settings.toml";
    public const string ToolsDirectoryName = "tools";
    public const string ModesDirectoryName = "modes";

    public static string GetDefaultDirectory() =>
        GetDefaultDirectory(ApplicationLocation.DistributionDirectory);

    internal static string GetDefaultDirectory(string applicationBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);
        var portableDirectory = Path.Combine(
            Path.GetFullPath(applicationBaseDirectory),
            PortableDataDirectoryName);
        if (Directory.Exists(portableDirectory))
        {
            return portableDirectory;
        }

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
        settings.FontFamily = string.IsNullOrWhiteSpace(settings.FontFamily)
            ? AzunoteSettings.DefaultFontFamily
            : settings.FontFamily.Trim();
        settings.FontSize = double.IsFinite(settings.FontSize) && settings.FontSize > 0
            ? settings.FontSize
            : AzunoteSettings.DefaultFontSize;
        settings.Debug ??= new();
        settings.Debug.Validate();
        settings.Tools ??= new();
        settings.Tools.Validate();
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

        settings.Debug ??= new();
        settings.Debug.Validate();
        settings.Tools ??= new();
        settings.Tools.Validate();

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

        var defaultFiles = BundledDefaultAppData.GetFiles(ModesDirectoryName);
        Directory.CreateDirectory(modesDirectory);
        foreach (var relativePath in defaultFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationPath = Path.Combine(modesDirectory, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            await using var source = BundledDefaultAppData.OpenRead(
                Path.Combine(ModesDirectoryName, relativePath));
            if (source is null)
            {
                continue;
            }

            await using var destination = File.Create(destinationPath);
            await source.CopyToAsync(destination, cancellationToken);
        }
    }

    private static async Task EnsureDefaultSettingsAsync(
        string settingsPath,
        CancellationToken cancellationToken)
    {
        await using var source = BundledDefaultAppData.OpenRead(SettingsFileName);
        if (source is null)
        {
            await File.WriteAllTextAsync(
                settingsPath,
                string.Empty,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            return;
        }

        await using var destination = File.Create(settingsPath);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static async Task EnsureDefaultToolsAsync(
        string settingsDirectory,
        CancellationToken cancellationToken)
    {
        var toolsDirectory = GetToolsDirectoryPath(settingsDirectory);
        foreach (var relativePath in BundledDefaultAppData.GetFiles(ToolsDirectoryName))
        {
            cancellationToken.ThrowIfCancellationRequested();
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

            await using var source = BundledDefaultAppData.OpenRead(
                Path.Combine(ToolsDirectoryName, relativePath));
            if (source is null)
            {
                continue;
            }

            await using var destination = File.Create(destinationPath);
            await source.CopyToAsync(destination, cancellationToken);
        }
    }
}
