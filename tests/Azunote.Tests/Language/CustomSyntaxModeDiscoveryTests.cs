using Azunyan.Core;
using Xunit;

namespace Azunote.Tests;

public sealed class CustomSyntaxModeDiscoveryTests
{
    [Fact]
    public async Task The_default_files_include_the_ini_mode_and_it_loads()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            await SettingsFileService.EnsureExistsAsync(root);
            await SettingsFileService.InstallDefaultFilesAsync(root);

            var modePath = Path.Combine(SettingsFileService.GetModesDirectoryPath(root), "ini.toml");
            Assert.True(File.Exists(modePath));

            var settings = await SettingsFileService.LoadAsync(root);
            var mode = Assert.Single(settings.CustomSyntaxModes, mode => mode.Id == "ini");
            Assert.Equal("INI", mode.DisplayName);
            Assert.Contains("*.ini", mode.Patterns!);
            Assert.Equal(Path.GetFullPath(modePath), mode.DefinitionPath);

            const string text = "; comment\nname = \"a ; b\"\nactive = true\nvalue = 42\n[server]\n";
            var spans = await mode.Syntax!.GetSyntaxAsync(
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

    [Fact]
    public async Task The_default_files_for_built_in_modes_change_nothing_until_uncommented()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            await SettingsFileService.EnsureExistsAsync(root);
            await SettingsFileService.InstallDefaultFilesAsync(root);

            var settings = await SettingsFileService.LoadAsync(root);
            var typeScript = Assert.Single(settings.CustomSyntaxModes, mode => mode.Id == "typescript");
            Assert.Null(typeScript.Syntax);
            Assert.Null(typeScript.WorkspacePatterns);
            Assert.Null(typeScript.LanguageServer);

            var catalog = LanguageModeCatalog.Create(settings.CustomSyntaxModes);
            var builtIn = LanguageModeCatalog.Create();
            Assert.Equal(builtIn.Entries.Count + 1, catalog.Entries.Count);
            Assert.True(catalog.TryGet("typescript", out var changed));
            Assert.True(builtIn.TryGet("typescript", out var original));
            Assert.Equal(original.Patterns, changed.Patterns);
            Assert.Equal(original.DisplayName, changed.DisplayName);
            Assert.Same(original.Provider?.GetType(), changed.Provider?.GetType());
            Assert.Null(changed.LanguageServer);
            Assert.Equal(typeScript.DefinitionPath, changed.DefinitionPath);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public async Task A_mode_takes_its_id_from_the_file_name_and_its_name_from_the_id()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var modesDirectory = SettingsFileService.GetModesDirectoryPath(root);
            Directory.CreateDirectory(modesDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(modesDirectory, "nim.toml"),
                """
                patterns = ["*.nim"]

                [[rules]]
                type = "line"
                classification = "comment"
                token = "#"
                """);

            var mode = Assert.Single((await SettingsFileService.LoadAsync(root)).CustomSyntaxModes);

            Assert.Equal("nim", mode.Id);
            Assert.Equal("nim", mode.DisplayName);
            Assert.Equal("nim", LanguageModeCatalog.Create([mode]).SelectForPath("main.nim"));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public async Task A_built_in_mode_reads_its_workspace_patterns_and_language_server()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var modesDirectory = SettingsFileService.GetModesDirectoryPath(root);
            Directory.CreateDirectory(modesDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(modesDirectory, "python.toml"),
                """
                workspacePatterns = ["pyproject.toml"]

                [languageServer]
                command = "pyright-langserver"
                args = ["--stdio"]
                path = "tools"
                allowOutsideWorkspace = true

                [languageServer.settings.python.analysis]
                typeCheckingMode = "basic"
                extraPaths = ["src", "lib"]
                depth = 2
                """);

            var mode = Assert.Single((await SettingsFileService.LoadAsync(root)).CustomSyntaxModes);

            Assert.Equal("python", mode.Id);
            Assert.Null(mode.Syntax);
            Assert.Equal(["pyproject.toml"], mode.WorkspacePatterns);
            var server = Assert.IsType<LanguageServerDefinition>(mode.LanguageServer);
            Assert.Equal("pyright-langserver", server.Command);
            Assert.Equal(["--stdio"], server.Arguments);
            Assert.Equal(["tools"], server.SearchPath);
            Assert.Equal(Path.GetFullPath(modesDirectory), server.DefinitionDirectory);
            Assert.True(server.AllowOutsideWorkspace);
            Assert.Equal(
                """{"python":{"analysis":{"typeCheckingMode":"basic","extraPaths":["src","lib"],"depth":2}}}""",
                server.Settings!.ToJsonString());
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public async Task A_built_in_mode_cannot_have_rules()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var modesDirectory = SettingsFileService.GetModesDirectoryPath(root);
            Directory.CreateDirectory(modesDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(modesDirectory, "json.toml"),
                """
                [[rules]]
                type = "literal"
                classification = "keyword"
                token = "null"
                """);

            var exception = await Assert.ThrowsAsync<SettingsFileException>(
                () => SettingsFileService.LoadAsync(root));
            Assert.Contains("cannot have rules", exception.Message, StringComparison.Ordinal);
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
