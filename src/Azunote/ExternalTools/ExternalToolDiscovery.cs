namespace Azunote;
/// <summary>
/// Discovers external tool definitions below a settings directory. Directory
/// names become menu folders, while files ending in <c>.tool.toml</c> are
/// tool leaves. A directory ending in <c>.tool</c> with a manifest.toml file
/// is a bundled tool leaf and is not traversed as a menu folder.
/// Hidden and system entries are skipped, as are reparse points such as
/// junctions and directory symbolic links.
/// </summary>
public static class ExternalToolDiscovery
{
    private const string ToolFileSuffix = ".tool.toml";
    private const string ToolDirectorySuffix = ".tool";
    private const string ManifestFileName = "manifest.toml";

    /// <summary>
    /// Attributes that keep a directory out of the scan. Hidden covers the
    /// .git directory Git for Windows creates when a tool collection is
    /// cloned into the tools folder, whose contents are both irrelevant and
    /// expensive to walk. Reparse points are skipped because the scan has no
    /// other guard against a junction that points back at an ancestor.
    /// </summary>
    private const FileAttributes SkippedDirectoryAttributes =
        FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint;

    private const FileAttributes SkippedFileAttributes =
        FileAttributes.Hidden | FileAttributes.System;

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

        // Enumerating through DirectoryInfo keeps the attributes that come
        // back with each entry, so skipping one costs no extra query.
        var directoryInfo = new DirectoryInfo(directory);
        var nodes = new List<ExternalToolMenuNode>();
        foreach (var childDirectory in directoryInfo.EnumerateDirectories()
                     .Where(child => (child.Attributes & SkippedDirectoryAttributes) == 0)
                     .OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var childDirectoryName = childDirectory.Name;
            var manifestPath = Path.Combine(childDirectory.FullName, ManifestFileName);
            if (childDirectoryName.EndsWith(ToolDirectorySuffix, StringComparison.OrdinalIgnoreCase)
                && File.Exists(manifestPath))
            {
                var tool = await LoadToolAsync(
                    manifestPath,
                    TrimSuffix(childDirectoryName, ToolDirectorySuffix),
                    childDirectory.FullName,
                    cancellationToken);
                tools.Add(tool);
                nodes.Add(new ExternalToolMenuNode(tool.Name, tool));
                continue;
            }

            var children = await LoadDirectoryAsync(
                childDirectory.FullName,
                tools,
                cancellationToken);
            if (children.Count > 0)
            {
                nodes.Add(new ExternalToolMenuNode(childDirectoryName, children: children));
            }
        }

        foreach (var toolFile in directoryInfo.EnumerateFiles()
                     .Where(file => (file.Attributes & SkippedFileAttributes) == 0
                         && file.Name.EndsWith(
                             ToolFileSuffix,
                             StringComparison.OrdinalIgnoreCase))
                     .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fallbackName = TrimSuffix(toolFile.Name, ToolFileSuffix);
            var tool = await LoadToolAsync(
                toolFile.FullName,
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
