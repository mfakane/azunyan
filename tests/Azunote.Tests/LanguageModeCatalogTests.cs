using Azunyan.Syntax;
using Xunit;

namespace Azunote.Tests;

public sealed class LanguageModeCatalogTests
{
    [Fact]
    public void Catalog_selects_the_most_specific_matching_mode()
    {
        var custom = new SyntaxLanguageDefinition(
            "custom-config",
            "Custom config",
            ["*.config"],
            [new LiteralSyntaxRule("true", "keyword")]);
        var catalog = LanguageModeCatalog.Create([Mode(custom)]);

        Assert.Equal("custom-config", catalog.SelectForPath("settings.config"));
        Assert.Equal("plain-text", catalog.SelectForPath("readme.txt"));
    }

    [Fact]
    public void A_built_in_id_changes_only_the_settings_given()
    {
        var server = new LanguageServerDefinition("json-server", ["--stdio"], [], null, null, null, null);
        var change = new CustomLanguageMode(
            "json",
            Path.GetFullPath("json.toml"),
            null,
            "My JSON",
            SyntaxLanguageDefinition.NormalizePatterns(["*.custom-json"]),
            null,
            ["package.json"],
            server);
        var builtIn = LanguageModeCatalog.Create();
        var catalog = LanguageModeCatalog.Create([change]);

        Assert.True(builtIn.TryGet("json", out var original));
        Assert.True(catalog.TryGet("json", out var json));
        Assert.Equal(builtIn.Entries.Count, catalog.Entries.Count);
        Assert.Equal("My JSON", json.DisplayName);
        Assert.Equal("json", catalog.SelectForPath("data.custom-json"));
        Assert.NotEqual("json", catalog.SelectForPath("data.json"));
        Assert.Equal([".custom-json"], json.FileExtensions);
        Assert.Equal(original.CompletionTriggers, json.CompletionTriggers);
        Assert.Same(original.Provider?.GetType(), json.Provider?.GetType());
        Assert.Equal(["package.json"], json.WorkspacePatterns);
        Assert.Same(server, json.LanguageServer);
        Assert.Equal(change.DefinitionPath, json.DefinitionPath);
        Assert.Equal("yaml", catalog.SelectForPath("config.yml"));
    }

    [Fact]
    public void File_filters_normalize_extensions_and_include_supported_and_all_entries()
    {
        var catalog = LanguageModeCatalog.Create();
        var filters = catalog.GetFileDialogFilters();

        Assert.Equal("supported", filters[0].Id);
        Assert.Contains(filters, filter => filter.Id == "plain-text");
        Assert.Equal("all", filters[^1].Id);
        Assert.Contains(".txt", filters.Single(filter => filter.Id == "plain-text").Extensions);
        Assert.Contains(".jsonc", filters.Single(filter => filter.Id == "json").Extensions);
        Assert.Contains(".json5", filters.Single(filter => filter.Id == "json").Extensions);
        Assert.Contains(".xml", filters.Single(filter => filter.Id == "xml").Extensions);
    }

    [Theory]
    [InlineData("notes.md", true)]
    [InlineData("notes.txt", true)]
    [InlineData("program.cs", true)]
    [InlineData("settings.jsonc", true)]
    [InlineData("settings.json5", true)]
    [InlineData("settings.xml", true)]
    [InlineData("icon.png", false)]
    [InlineData("archive.zip", false)]
    public void Known_files_are_the_ones_a_language_mode_covers(string path, bool expected)
    {
        var catalog = LanguageModeCatalog.Create();

        Assert.Equal(expected, catalog.IsKnownFile(path));
    }

    [Theory]
    [InlineData("settings.jsonc")]
    [InlineData("settings.json5")]
    public void Json_family_files_select_the_json_mode(string path)
    {
        Assert.Equal("json", LanguageModeCatalog.Create().SelectForPath(path));
    }

    [Fact]
    public void Xml_files_select_the_xml_mode()
    {
        Assert.Equal("xml", LanguageModeCatalog.Create().SelectForPath("settings.xml"));
    }

    [Fact]
    public void A_custom_mode_extends_the_known_files()
    {
        var custom = new SyntaxLanguageDefinition(
            "custom",
            "Custom",
            ["*.custom"],
            [new LiteralSyntaxRule("true", "keyword")]);

        Assert.False(LanguageModeCatalog.Create().IsKnownFile("notes.custom"));
        Assert.True(LanguageModeCatalog.Create([Mode(custom)]).IsKnownFile("notes.custom"));
    }

    private static CustomLanguageMode Mode(SyntaxLanguageDefinition syntax) =>
        new(
            syntax.Id,
            Path.GetFullPath($"{syntax.Id}.toml"),
            syntax,
            syntax.DisplayName,
            syntax.Patterns,
            syntax.CompletionTriggerCharacters,
            [],
            null);
}
