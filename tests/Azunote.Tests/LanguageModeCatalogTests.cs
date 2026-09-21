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
        var catalog = LanguageModeCatalog.Create([custom]);

        Assert.Equal("custom-config", catalog.SelectForPath("settings.config"));
        Assert.Equal("plain-text", catalog.SelectForPath("readme.txt"));
    }

    [Fact]
    public void Built_in_mode_wins_over_custom_duplicate_ids()
    {
        var duplicate = new SyntaxLanguageDefinition(
            "json",
            "Custom JSON",
            ["*.custom-json"],
            [new LiteralSyntaxRule("true", "keyword")]);
        var catalog = LanguageModeCatalog.Create([duplicate]);

        Assert.DoesNotContain(catalog.Entries, entry => entry.DisplayName == "Custom JSON");
        Assert.Equal("json", catalog.SelectForPath("data.json"));
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
    }

    [Theory]
    [InlineData("notes.md", true)]
    [InlineData("notes.txt", true)]
    [InlineData("program.cs", true)]
    [InlineData("settings.jsonc", true)]
    [InlineData("settings.json5", true)]
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
    public void A_custom_mode_extends_the_known_files()
    {
        var custom = new SyntaxLanguageDefinition(
            "custom",
            "Custom",
            ["*.custom"],
            [new LiteralSyntaxRule("true", "keyword")]);

        Assert.False(LanguageModeCatalog.Create().IsKnownFile("notes.custom"));
        Assert.True(LanguageModeCatalog.Create([custom]).IsKnownFile("notes.custom"));
    }
}
