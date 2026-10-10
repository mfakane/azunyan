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

    private static string ResolveDistributionDirectory() =>
        ResolveDistributionDirectory(
            Environment.GetEnvironmentVariable(ShimRootVariable),
            Environment.ProcessPath,
            AppContext.BaseDirectory);

    internal static string ResolveDistributionDirectory(
        string? shimRoot, string? processPath, string baseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(shimRoot) && Directory.Exists(shimRoot))
        {
            return Path.GetFullPath(shimRoot);
        }

        // Started through the .NET host (dotnet Azunote.dll, or a test host on
        // a platform without an apphost for it, such as win-arm64), the process
        // is dotnet.exe, whose folder has nothing to do with this copy of Azunote.
        if (string.IsNullOrEmpty(processPath) || IsDotnetHost(processPath))
        {
            return baseDirectory;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(processPath));
        return string.IsNullOrEmpty(directory) ? baseDirectory : directory;
    }

    private static bool IsDotnetHost(string processPath) =>
        string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetFileName(processPath), "dotnet", StringComparison.Ordinal);
}
