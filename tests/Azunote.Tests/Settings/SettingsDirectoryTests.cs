using Xunit;

namespace Azunote.Tests.Settings;

public sealed class SettingsDirectoryTests
{
    [Fact]
    public void Default_directory_prefers_portable_appdata_directory()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var portableDirectory = Path.Combine(
                root,
                SettingsFileService.PortableDataDirectoryName);
            Directory.CreateDirectory(portableDirectory);

            Assert.Equal(
                Path.GetFullPath(portableDirectory),
                SettingsFileService.GetDefaultDirectory(root));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public void Default_directory_falls_back_to_local_appdata_without_portable_directory()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = Path.GetTempPath();
            }

            Assert.Equal(
                Path.Combine(localAppData, SettingsFileService.SettingsDirectoryName),
                SettingsFileService.GetDefaultDirectory(root));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"azunote-settings-directory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
