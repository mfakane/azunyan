using Azunyan.Core;
using Xunit;

namespace Azunote.Tests;

public sealed class CustomSyntaxModeDiscoveryTests
{
    [Fact]
    public async Task Ensure_exists_copies_the_bundled_ini_mode_and_loads_it()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            await SettingsFileService.EnsureExistsAsync(root);

            var modePath = Path.Combine(SettingsFileService.GetModesDirectoryPath(root), "ini.toml");
            Assert.True(File.Exists(modePath));

            var settings = await SettingsFileService.LoadAsync(root);
            var mode = Assert.Single(settings.CustomSyntaxModes);
            Assert.Equal("ini", mode.Id);
            Assert.Equal("INI", mode.DisplayName);
            Assert.Contains("*.ini", mode.Patterns);
            Assert.Equal(Path.GetFullPath(modePath), mode.DefinitionPath);

            const string text = "; comment\nname = \"a ; b\"\nactive = true\nvalue = 42\n[server]\n";
            var spans = await mode.GetSyntaxAsync(
                new EditorProviderContext(
                    new TextSnapshot(text),
                    0,
                    TextSelection.Caret(0)));

            Assert.Contains(
                spans,
                span => text[span.Range.Start..span.Range.End] == "; comment"
                    && span.Classification == "comment");
            Assert.Contains(
                spans,
                span => text[span.Range.Start..span.Range.End] == "\"a ; b\""
                    && span.Classification == "string");
            Assert.Contains(
                spans,
                span => text[span.Range.Start..span.Range.End] == "[server]"
                    && span.Classification == "heading");
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public async Task User_mode_files_are_discovered_recursively()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var modesDirectory = Path.Combine(SettingsFileService.GetModesDirectoryPath(root), "custom");
            Directory.CreateDirectory(modesDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(modesDirectory, "example.toml"),
                """
                id = "example"
                displayName = "Example"
                patterns = ["configs/*.example"]

                [[rules]]
                type = "literal"
                classification = "keyword"
                token = "hello"
                """);

            var settings = await SettingsFileService.LoadAsync(root);

            var mode = Assert.Single(settings.CustomSyntaxModes);
            Assert.Equal("example", mode.Id);
            Assert.Equal(["configs/*.example"], mode.Patterns);
            Assert.Equal(
                Path.GetFullPath(Path.Combine(modesDirectory, "example.toml")),
                mode.DefinitionPath);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"azunote-modes-{Guid.NewGuid():N}");
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
