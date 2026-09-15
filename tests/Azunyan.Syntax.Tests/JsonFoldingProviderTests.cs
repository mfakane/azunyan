using Azunyan.Core;
using Xunit;

namespace Azunyan.Syntax.Tests;

public sealed class JsonFoldingProviderTests
{
    [Fact]
    public async Task Objects_and_arrays_fold_between_their_brackets()
    {
        const string text =
            "{\n"
            + "  \"name\": \"azunote\",\n"
            + "  \"items\": [\n"
            + "    1,\n"
            + "    2\n"
            + "  ]\n"
            + "}";
        var snapshot = new TextSnapshot(text);

        var folds = await GetAsync(snapshot);

        Assert.Equal(
            ["\n    1,\n    2\n  ", "\n  \"name\": \"azunote\",\n  \"items\": [\n    1,\n    2\n  ]\n"],
            folds.Select(fold => snapshot.GetText(fold.Range)));
        Assert.Equal(["json-array:$/items:0", "json-object:$:0"], folds.Select(fold => fold.Id));
        Assert.All(folds, fold => Assert.Equal(" … ", fold.Placeholder));
    }

    [Fact]
    public async Task A_container_on_one_line_is_not_folded()
    {
        const string text =
            "{\n"
            + "  \"point\": {\"x\": 1, \"y\": 2},\n"
            + "  \"tags\": []\n"
            + "}";
        var snapshot = new TextSnapshot(text);

        var fold = Assert.Single(await GetAsync(snapshot));

        Assert.Equal("json-object:$:0", fold.Id);
    }

    [Fact]
    public async Task Nested_containers_are_named_after_their_place()
    {
        const string text =
            "{\n"
            + "  \"jobs\": [\n"
            + "    {\n"
            + "      \"id\": 1\n"
            + "    },\n"
            + "    {\n"
            + "      \"id\": 2\n"
            + "    }\n"
            + "  ]\n"
            + "}";
        var snapshot = new TextSnapshot(text);

        var folds = await GetAsync(snapshot);

        Assert.Equal(
            [
                "json-object:$/jobs/0:0",
                "json-object:$/jobs/1:0",
                "json-array:$/jobs:0",
                "json-object:$:0"
            ],
            folds.Select(fold => fold.Id));
    }

    [Fact]
    public async Task Brackets_inside_strings_are_not_structure()
    {
        const string text =
            "{\n"
            + "  \"pattern\": \"{ not an object [\\\" either\",\n"
            + "  \"escaped\": \"a\\\\\"\n"
            + "}";
        var snapshot = new TextSnapshot(text);

        var fold = Assert.Single(await GetAsync(snapshot));

        Assert.Equal("json-object:$:0", fold.Id);
        Assert.Equal(text[1..^1], snapshot.GetText(fold.Range));
    }

    [Fact]
    public async Task An_empty_container_spanning_lines_is_not_folded()
    {
        var snapshot = new TextSnapshot("{\n}\n");

        Assert.Empty(await GetAsync(snapshot));
    }

    [Fact]
    public async Task Unbalanced_text_does_not_throw()
    {
        var snapshot = new TextSnapshot("{\n  \"a\": [1,\n");

        Assert.Empty(await GetAsync(snapshot));
    }

    [Fact]
    public void Json_language_definition_supplies_the_provider()
    {
        Assert.IsType<JsonFoldingProvider>(BuiltInSyntaxLanguages.Json.FoldingProvider);
    }

    private static async Task<IReadOnlyList<FoldRange>> GetAsync(TextSnapshot snapshot) =>
        await new JsonFoldingProvider().GetFoldsAsync(
            new EditorProviderContext(snapshot, 0, TextSelection.Caret(0)));
}
