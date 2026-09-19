namespace Azunote;

/// <summary>
/// The two directories an Azunote build reads from. They are the same one in a
/// development or Native AOT build, and are not in the single-file portable
/// build: its host extracts the bundle under the temporary directory and runs
/// from there, so what Azunote was published with and what a user placed next
/// to the executable stop being the same place.
/// </summary>
internal static class ApplicationLocation
{
    /// <summary>
    /// Where the executable a user launched sits. This is the directory a
    /// portable <c>appdata</c> and the distributed legal documents are looked
    /// for in, because they belong to the copy of Azunote, not to its bundle.
    /// </summary>
    internal static string DistributionDirectory { get; } = ResolveDistributionDirectory();

    /// <summary>
    /// Where the files Azunote was published with are read from, such as its
    /// assets and the bundled default application data.
    /// </summary>
    internal static string BundleDirectory => AppContext.BaseDirectory;

    private static string ResolveDistributionDirectory()
    {
        var processPath = Environment.ProcessPath;
        var directory = string.IsNullOrEmpty(processPath)
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(processPath));
        return string.IsNullOrEmpty(directory) ? AppContext.BaseDirectory : directory;
    }
}
