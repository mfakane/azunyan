using Azunyan.Core;
using Xunit;

namespace Azunote.Tests;

public sealed class AzunoteConfigurationCompletionProviderTests
{
    [Theory]
    [InlineData(@"C:\Users\test\settings.toml", "azunote.settings")]
    [InlineData(@"C:\Users\test\tools\format.tool.toml", "azunote.tool")]
    [InlineData(@"C:\Users\test\modes\custom.toml", "azunote.mode")]
    public void Schema_catalog_selects_the_file_specific_schema(string path, string expectedId)
    {
        var schemas = AzunoteSchemaCatalog.ForPath(path);

        Assert.Single(schemas);
        Assert.Equal(expectedId, schemas[0].Id);
    }

    [Fact]
    public async Task Tool_schema_completes_root_fields()
    {
        const string text = "com";
        var provider = new AzunoteConfigurationCompletionProvider(
            [AzunoteSchemaCatalog.ExternalTool]);

        var result = await provider.GetCompletionsAsync(
            new EditorProviderContext(
                new TextSnapshot(text),
                text.Length,
                TextSelection.Caret(text.Length)));

        Assert.NotNull(result);
        Assert.Equal(new TextRange(0, 3), result!.ReplacementRange);
        Assert.Contains(result.Items, item => item.Label == "command");
    }

    [Fact]
    public async Task Tool_schema_completes_enum_values_inside_a_string()
    {
        const string text = "input = \"Fi";
        var provider = new AzunoteConfigurationCompletionProvider(
            [AzunoteSchemaCatalog.ExternalTool]);

        var result = await provider.GetCompletionsAsync(
            new EditorProviderContext(
                new TextSnapshot(text),
                text.Length,
                TextSelection.Caret(text.Length)));

        Assert.NotNull(result);
        Assert.Equal(new TextRange(8, 3), result!.ReplacementRange);
        var item = Assert.Single(result.Items, candidate => candidate.Label == "\"FilePath\"");
        Assert.Equal("\"FilePath\"", item.InsertText);
    }

    [Fact]
    public async Task Mode_schema_completes_rule_enum_values_in_array_tables()
    {
        const string text = "[[rules]]\ntype = \"re";
        var provider = new AzunoteConfigurationCompletionProvider(
            [AzunoteSchemaCatalog.CustomMode]);

        var result = await provider.GetCompletionsAsync(
            new EditorProviderContext(
                new TextSnapshot(text),
                text.Length,
                TextSelection.Caret(text.Length)));

        Assert.NotNull(result);
        Assert.Contains(result!.Items, item => item.Label == "\"regex\"");
    }
}
