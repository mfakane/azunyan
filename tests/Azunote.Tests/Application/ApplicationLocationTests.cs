using Xunit;

namespace Azunote.Tests;

public sealed class ApplicationLocationTests
{
    private static readonly string BaseDirectory = Path.GetFullPath(Path.Combine("C:\\", "apps", "Azunote")) + Path.DirectorySeparatorChar;

    [Fact]
    public void Process_directory_is_the_distribution_directory()
    {
        var processPath = Path.Combine("C:\\", "portable", "Azunote.exe");

        Assert.Equal(
            Path.GetDirectoryName(Path.GetFullPath(processPath)),
            ApplicationLocation.ResolveDistributionDirectory(null, processPath, BaseDirectory));
    }

    [Theory]
    [InlineData("dotnet.exe")]
    [InlineData("DOTNET.EXE")]
    public void Dotnet_host_falls_back_to_the_application_base_directory(string host)
    {
        var processPath = Path.Combine("C:\\", "Program Files", "dotnet", host);

        Assert.Equal(
            BaseDirectory,
            ApplicationLocation.ResolveDistributionDirectory(null, processPath, BaseDirectory));
    }

    [Fact]
    public void Missing_process_path_falls_back_to_the_application_base_directory() =>
        Assert.Equal(
            BaseDirectory,
            ApplicationLocation.ResolveDistributionDirectory(null, null, BaseDirectory));

    [Fact]
    public void Shim_root_wins_when_it_exists()
    {
        var shimRoot = Path.GetTempPath();

        Assert.Equal(
            Path.GetFullPath(shimRoot),
            ApplicationLocation.ResolveDistributionDirectory(
                shimRoot, Path.Combine("C:\\", "portable", "app", "Azunote.exe"), BaseDirectory));
    }
}
