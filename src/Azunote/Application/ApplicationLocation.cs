namespace Azunote;

/// <summary>
/// The two directories an Azunote build reads from. They are the same one in a
/// development or packaged build, and are not in the portable build: its
/// published files sit in a subdirectory that a shim carrying the application's
/// name starts, so what Azunote was published with and what a user placed
/// beside the executable they launched stop being the same place.
/// </summary>
internal static class ApplicationLocation
{
    /// <summary>
    /// Set by the publish shim to the directory it was started from. A shim
    /// starts the application from a subdirectory, so nothing the application
    /// can read about itself names the folder the user sees.
    /// </summary>
    private const string ShimRootVariable = "PUBLISH_SHIM_ROOT";

    /// <summary>
    /// Set by the publish shim to the path it was invoked as.
    /// </summary>
    private const string ShimExecutableVariable = "PUBLISH_SHIM_EXE";

    /// <summary>
    /// The executable a user starts Azunote by, which is the shim rather than
    /// the relocated application wherever one stands in front of it.
    /// </summary>
    internal static string ExecutablePath { get; } = ResolveExecutablePath();

    /// <summary>
    /// The folder a user sees. This is where a portable <c>appdata</c> and the
    /// distributed legal documents are looked for, because they belong to the
    /// copy of Azunote rather than to what it was published with.
    /// </summary>
    internal static string DistributionDirectory { get; } = ResolveDistributionDirectory();

    /// <summary>
    /// Where the files Azunote was published with are read from, such as its
    /// assets and the bundled default application data.
    /// </summary>
    internal static string BundleDirectory => AppContext.BaseDirectory;

    private static string ResolveExecutablePath()
    {
        var shimExecutable = Environment.GetEnvironmentVariable(ShimExecutableVariable);
        return string.IsNullOrWhiteSpace(shimExecutable) || !File.Exists(shimExecutable)
            ? Environment.ProcessPath ?? string.Empty
            : Path.GetFullPath(shimExecutable);
    }

    private static string ResolveDistributionDirectory()
    {
        var shimRoot = Environment.GetEnvironmentVariable(ShimRootVariable);
        if (!string.IsNullOrWhiteSpace(shimRoot) && Directory.Exists(shimRoot))
        {
            return Path.GetFullPath(shimRoot);
        }

        var processPath = Environment.ProcessPath;
        var directory = string.IsNullOrEmpty(processPath)
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(processPath));
        return string.IsNullOrEmpty(directory) ? AppContext.BaseDirectory : directory;
    }
}
