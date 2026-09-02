namespace Azunote;
/// <summary>
/// Discovers external tool definitions below a settings directory. Directory
/// names become menu folders, while files ending in <c>.tool.toml</c> are
/// tool leaves. A directory ending in <c>.tool</c> with a manifest.toml file
/// is a bundled tool leaf and is not traversed as a menu folder.
/// </summary>
public static class ExternalToolDiscovery
{
    private const string ToolFileSuffix = ".tool.toml";
    private const string ToolDirectorySuffix = ".tool";
    private const string ManifestFileName = "manifest.toml";

    public static async Task<ExternalToolCatalog> LoadAsync(
        string toolsDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolsDirectory);

        var fullDirectory = Path.GetFullPath(toolsDirectory);
        if (!Directory.Exists(fullDirectory))
        {
            return new ExternalToolCatalog([], []);
        }

        var tools = new List<ExternalToolSettings>();
        var menu = await LoadDirectoryAsync(fullDirectory, tools, cancellationToken);
        return new ExternalToolCatalog(menu, tools);
    }

    private static async Task<IReadOnlyList<ExternalToolMenuNode>> LoadDirectoryAsync(
        string directory,
        ICollection<ExternalToolSettings> tools,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nodes = new List<ExternalToolMenuNode>();
        foreach (var childDirectory in Directory.EnumerateDirectories(directory)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var childDirectoryName = Path.GetFileName(childDirectory);
            var manifestPath = Path.Combine(childDirectory, ManifestFileName);
            if (childDirectoryName.EndsWith(ToolDirectorySuffix, StringComparison.OrdinalIgnoreCase)
                && File.Exists(manifestPath))
            {
                var tool = await LoadToolAsync(
                    manifestPath,
                    TrimSuffix(childDirectoryName, ToolDirectorySuffix),
                    childDirectory,
                    cancellationToken);
                tools.Add(tool);
                nodes.Add(new ExternalToolMenuNode(tool.Name, tool));
                continue;
            }

            var children = await LoadDirectoryAsync(childDirectory, tools, cancellationToken);
            if (children.Count > 0)
            {
                nodes.Add(new ExternalToolMenuNode(childDirectoryName, children: children));
            }
        }

        foreach (var toolPath in Directory.EnumerateFiles(directory)
                     .Where(path => Path.GetFileName(path).EndsWith(
                         ToolFileSuffix,
                         StringComparison.OrdinalIgnoreCase))
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fallbackName = TrimSuffix(Path.GetFileName(toolPath), ToolFileSuffix);
            var tool = await LoadToolAsync(
                toolPath,
                fallbackName,
                directory,
                cancellationToken);
            tools.Add(tool);
            nodes.Add(new ExternalToolMenuNode(tool.Name, tool));
        }

        return nodes;
    }

    private static async Task<ExternalToolSettings> LoadToolAsync(
        string path,
        string fallbackName,
        string definitionDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            var tool = await SettingsFileService.DeserializeAsync<ExternalToolSettings>(
                path,
                AzunoteTomlSerializerContext.Default.ExternalToolSettings,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(tool.Name))
            {
                tool.Name = fallbackName;
            }

            tool.DefinitionDirectory = Path.GetFullPath(definitionDirectory);
            tool.DefinitionPath = Path.GetFullPath(path);
            tool.Validate();
            return tool;
        }
        catch (SettingsFileException exception)
        {
            throw new SettingsFileException(
                $"Could not load external tool definition '{path}': {exception.Message}",
                exception);
        }
    }

    private static string TrimSuffix(string value, string suffix) =>
        value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? value[..^suffix.Length]
            : value;
}
