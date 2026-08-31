using Xunit;

namespace Azunote.Tests.Settings;

public sealed class DebugLoggingSettingsTests
{
    [Fact]
    public async Task Settings_service_loads_and_normalizes_debug_logging_categories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"azunote-debug-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(
                SettingsFileService.GetSettingsFilePath(root),
                """
                [debug]
                logging = ["key", "render", "key"]

                [terminal]
                command = "wt.exe"

                [explorer]
                command = "explorer.exe"
                """);

            var settings = await SettingsFileService.LoadAsync(root);

            Assert.Equal(["render", "key"], settings.Debug.Logging);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Settings_service_accepts_all_as_the_only_normalized_category()
    {
        var root = Path.Combine(Path.GetTempPath(), $"azunote-debug-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(
                SettingsFileService.GetSettingsFilePath(root),
                """
                [debug]
                logging = ["render", "all", "input"]

                [terminal]
                command = "wt.exe"

                [explorer]
                command = "explorer.exe"
                """);

            var settings = await SettingsFileService.LoadAsync(root);

            Assert.Equal(["all"], settings.Debug.Logging);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Settings_service_rejects_unknown_debug_logging_categories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"azunote-debug-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(
                SettingsFileService.GetSettingsFilePath(root),
                """
                [debug]
                logging = ["clipboard", "not-a-category"]

                [terminal]
                command = "wt.exe"

                [explorer]
                command = "explorer.exe"
                """);

            var exception = await Assert.ThrowsAsync<SettingsFileException>(
                () => SettingsFileService.LoadAsync(root));

            Assert.Contains("debug.logging", exception.Message, StringComparison.Ordinal);
            Assert.Contains("not-a-category", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Settings_service_round_trips_debug_logging_categories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"azunote-debug-settings-{Guid.NewGuid():N}");
        try
        {
            var settings = new AzunoteSettings
            {
                Debug = new AzunoteDebugSettings
                {
                    Logging = ["key", "clipboard", "render"]
                }
            };

            await SettingsFileService.SaveAsync(root, settings);

            var settingsText = await File.ReadAllTextAsync(
                SettingsFileService.GetSettingsFilePath(root));
            Assert.Contains("[debug]", settingsText, StringComparison.Ordinal);
            Assert.Contains(
                "logging = [\"render\", \"clipboard\", \"key\"]",
                settingsText,
                StringComparison.Ordinal);
            var loaded = await SettingsFileService.LoadAsync(root);
            Assert.Equal(["render", "clipboard", "key"], loaded.Debug.Logging);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
