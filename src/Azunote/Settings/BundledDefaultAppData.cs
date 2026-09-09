using System.Reflection;

namespace Azunote;

internal static class BundledDefaultAppData
{
    private const string Prefix = "Azunote.DefaultAppData.";
    private static readonly Assembly Assembly = typeof(BundledDefaultAppData).Assembly;

    internal static IReadOnlyList<string> GetFiles(string directoryName)
    {
        var diskDirectory = GetDiskPath(directoryName);
        if (Directory.Exists(diskDirectory))
        {
            return Directory.EnumerateFiles(diskDirectory, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(diskDirectory, path))
                .ToArray();
        }

        var resourcePrefix = Prefix + directoryName + "\\";
        return Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(resourcePrefix, StringComparison.Ordinal))
            .Select(name => name[resourcePrefix.Length..])
            .ToArray();
    }

    internal static Stream? OpenRead(string relativePath)
    {
        var diskPath = GetDiskPath(relativePath);
        if (File.Exists(diskPath))
        {
            return File.OpenRead(diskPath);
        }

        var resourceName = Prefix + relativePath.Replace('/', '\\');
        return Assembly.GetManifestResourceStream(resourceName);
    }

    private static string GetDiskPath(string relativePath) => Path.Combine(
        AppContext.BaseDirectory,
        "Resources",
        "DefaultAppData",
        relativePath);
}
