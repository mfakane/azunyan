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
    }
}
