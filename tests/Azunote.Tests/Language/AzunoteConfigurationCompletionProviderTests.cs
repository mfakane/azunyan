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
    public async Task Tool_schema_completes_launch_fields()
    {
        const string text = "[launch]\ncom";
        var provider = new AzunoteConfigurationCompletionProvider(
            [AzunoteSchemaCatalog.ExternalTool]);

        var result = await provider.GetCompletionsAsync(
            new EditorProviderContext(
                new TextSnapshot(text),
                text.Length,
                TextSelection.Caret(text.Length)));

        Assert.NotNull(result);
        Assert.Equal(new TextRange(9, 3), result!.ReplacementRange);
        Assert.Contains(result.Items, item => item.Label == "command");
    }

    [Fact]
    public async Task Settings_schema_completes_terminal_fields()
    {
        const string text = "[terminal]\nwork";
        var provider = new AzunoteConfigurationCompletionProvider(
            [AzunoteSchemaCatalog.Settings]);

        var result = await provider.GetCompletionsAsync(
            new EditorProviderContext(
                new TextSnapshot(text),
                text.Length,
                TextSelection.Caret(text.Length)));

        Assert.NotNull(result);
        Assert.Contains(result!.Items, item => item.Label == "workingDirectory");
    }

    [Fact]
    public async Task Settings_schema_completes_explorer_fields()
    {
        const string text = "[explorer]\nwork";
        var provider = new AzunoteConfigurationCompletionProvider(
            [AzunoteSchemaCatalog.Settings]);

        var result = await provider.GetCompletionsAsync(
            new EditorProviderContext(
                new TextSnapshot(text),
                text.Length,
                TextSelection.Caret(text.Length)));

        Assert.NotNull(result);
        Assert.Contains(result!.Items, item => item.Label == "workingDirectory");
    }

    [Fact]
    public async Task Settings_schema_completes_debug_logging_field()
    {
        const string text = "[debug]\nlog";
        var provider = new AzunoteConfigurationCompletionProvider(
            [AzunoteSchemaCatalog.Settings]);

        var result = await provider.GetCompletionsAsync(
            new EditorProviderContext(
                new TextSnapshot(text),
                text.Length,
                TextSelection.Caret(text.Length)));

        Assert.NotNull(result);
        Assert.Contains(result!.Items, item => item.Label == "logging");
    }

    [Fact]
    public async Task Tool_schema_completes_enum_values_inside_a_string()
    {
        const string text = "[launch]\ninput = \"fi";
        var provider = new AzunoteConfigurationCompletionProvider(
            [AzunoteSchemaCatalog.ExternalTool]);

        var result = await provider.GetCompletionsAsync(
            new EditorProviderContext(
                new TextSnapshot(text),
                text.Length,
                TextSelection.Caret(text.Length)));

        Assert.NotNull(result);
        Assert.Equal(new TextRange(17, 3), result!.ReplacementRange);
        var item = Assert.Single(result.Items, candidate => candidate.Label == "\"filePath\"");
        Assert.Equal("\"filePath\"", item.InsertText);
    }

    [Fact]
    public async Task Tool_schema_completes_output_actions_inside_a_string()
    {
        const string text = "[launch]\noutput = \"re";
        var provider = new AzunoteConfigurationCompletionProvider(
            [AzunoteSchemaCatalog.ExternalTool]);

        var result = await provider.GetCompletionsAsync(
            new EditorProviderContext(
                new TextSnapshot(text),
                text.Length,
                TextSelection.Caret(text.Length)));

        Assert.NotNull(result);
        Assert.Contains(result!.Items, item => item.Label == "\"replaceDocument\"");
        Assert.Contains(result.Items, item => item.Label == "\"replaceSelection\"");
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

    [Fact]
    public async Task Tool_schema_completes_all_built_in_placeholders_inside_an_argument()
    {
        const string text = "[launch]\nargs = [\"${";
        var provider = new AzunoteConfigurationCompletionProvider(
            [AzunoteSchemaCatalog.ExternalTool]);

        var result = await provider.GetCompletionsAsync(
            new EditorProviderContext(
                new TextSnapshot(text),
                text.Length,
                TextSelection.Caret(text.Length)));

        Assert.NotNull(result);
        Assert.Contains(result!.Items, item => item.Label == "${file}");
        Assert.Contains(result.Items, item => item.Label == "${fileBasename}");
        Assert.Contains(result.Items, item => item.Label == "${selectedText}");
        Assert.Contains(result.Items, item => item.Label == "${selectionEndColumn}");
        Assert.Contains(result.Items, item => item.Label == "${/}");
        var fileItem = Assert.Single(result.Items, item => item.Label == "${file}");
        Assert.Equal("${file}", fileItem.InsertText);
        Assert.Equal("Placeholder", fileItem.Detail);
        Assert.NotEmpty(fileItem.Documentation);
        Assert.Contains("Example: ${file} -> C:\\work\\notes\\current.azunote", fileItem.Documentation);
        Assert.Equal(
            TextRange.FromBounds(text.IndexOf("${", StringComparison.Ordinal), text.Length),
            result.ReplacementRange);
    }

    [Fact]
    public async Task Tool_schema_completes_placeholders_in_a_command_value()
    {
        const string text = "[launch]\ncommand = \"${doc";
        var provider = new AzunoteConfigurationCompletionProvider(
            [AzunoteSchemaCatalog.ExternalTool]);

        var result = await provider.GetCompletionsAsync(
            new EditorProviderContext(
                new TextSnapshot(text),
                text.Length,
                TextSelection.Caret(text.Length)));

        Assert.NotNull(result);
        Assert.Contains(result!.Items, item => item.Label == "${document}");
    }

    [Fact]
    public async Task Tool_schema_completes_input_placeholder_in_stdin()
    {
        const string text = "[launch]\nstdin = \"${in";
        var provider = new AzunoteConfigurationCompletionProvider(
            [AzunoteSchemaCatalog.ExternalTool]);

        var result = await provider.GetCompletionsAsync(
            new EditorProviderContext(
                new TextSnapshot(text),
                text.Length,
                TextSelection.Caret(text.Length)));

        Assert.NotNull(result);
        Assert.Contains(result!.Items, item => item.Label == "${input}");
    }

    [Fact]
    public async Task Tool_schema_completes_path_separator_shorthand()
    {
        const string text = "[launch]\nargs = [\"${/";
        var provider = new AzunoteConfigurationCompletionProvider(
            [AzunoteSchemaCatalog.ExternalTool]);

        var result = await provider.GetCompletionsAsync(
            new EditorProviderContext(
                new TextSnapshot(text),
                text.Length,
                TextSelection.Caret(text.Length)));

        Assert.NotNull(result);
        var item = Assert.Single(result!.Items, candidate => candidate.Label == "${/}");
        Assert.Equal("${/}", item.InsertText);
    }

    [Fact]
    public async Task A_closed_placeholder_replaces_only_its_name()
    {
        const string text = "[launch]\nargs = [\"${fi}\"]";
        var caret = text.IndexOf('}');
        var provider = new AzunoteConfigurationCompletionProvider(
            [AzunoteSchemaCatalog.ExternalTool]);

        var result = await provider.GetCompletionsAsync(
            new EditorProviderContext(
                new TextSnapshot(text),
                caret,
                TextSelection.Caret(caret)));

        Assert.NotNull(result);
        var opening = text.IndexOf("${", StringComparison.Ordinal);
        Assert.Equal(TextRange.FromBounds(opening + 2, caret), result!.ReplacementRange);
        var item = Assert.Single(result.Items, candidate => candidate.Label == "${file}");
        Assert.Equal("file", item.InsertText);
    }

    [Fact]
    public async Task External_tool_schema_completes_current_environment_variables()
    {
        var environmentName = Environment.GetEnvironmentVariables()
            .Keys
            .OfType<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        Assert.NotNull(environmentName);

        var prefixLength = Math.Min(3, environmentName!.Length);
        var prefix = environmentName[..prefixLength];
        var text = $"[env]\nTEST = \"${{env:{prefix}";
        var provider = new AzunoteConfigurationCompletionProvider(
            [AzunoteSchemaCatalog.ExternalTool]);

        var result = await provider.GetCompletionsAsync(
            new EditorProviderContext(
                new TextSnapshot(text),
                text.Length,
                TextSelection.Caret(text.Length)));

        Assert.NotNull(result);
        Assert.Contains(
            result!.Items,
            item => string.Equals(
                item.Label,
                $"${{env:{environmentName}}}",
                StringComparison.OrdinalIgnoreCase));
        var selected = result.Items.First(item => string.Equals(
            item.Label,
            $"${{env:{environmentName}}}",
            StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Environment variable", selected.Detail);
        Assert.Equal("${env:" + environmentName + "}", selected.InsertText);
        Assert.Contains(
            $"Example: ${{env:{environmentName}}} -> <value of {environmentName}>",
            selected.Documentation);
    }

    [Fact]
    public async Task Non_expandable_schema_values_do_not_offer_placeholders()
    {
        const string text = "[when]\nlanguages = [\"${f";
        var provider = new AzunoteConfigurationCompletionProvider(
            [AzunoteSchemaCatalog.ExternalTool]);

        var result = await provider.GetCompletionsAsync(
            new EditorProviderContext(
                new TextSnapshot(text),
                text.Length,
                TextSelection.Caret(text.Length)));

        Assert.Null(result);
    }
}
