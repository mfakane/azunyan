using Azunyan.Core;
using Xunit;

namespace Azunyan.Syntax.Tests;

public sealed class JsonFoldingProviderTests
{
    [Fact]
    public async Task Objects_and_arrays_fold_from_their_bracket_through_the_closing_line()
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

        Assert.Equal(["json-array:$/items:0", "json-object:$:0"], folds.Select(fold => fold.Id));
        Assert.Equal(
            [
                "\n    1,\n    2\n  ]\n",
                "\n  \"name\": \"azunote\",\n  \"items\": [\n    1,\n    2\n  ]\n}"
            ],
            folds.Select(fold => snapshot.GetText(fold.Range)));
        Assert.Equal([" … ]", " … }"], folds.Select(fold => fold.Placeholder));
    }

    [Fact]
    public async Task A_collapsed_container_reads_as_one_line()
    {
        const string text =
            "{\n"
            + "  \"items\": [\n"
            + "    1,\n"
            + "    2\n"
            + "  ],\n"
            + "  \"debug\": true\n"
            + "}\n";
        var snapshot = new TextSnapshot(text);
        var folds = await GetAsync(snapshot);

        var lines = Render(snapshot, folds.Single(fold => fold.Id == "json-array:$/items:0"));

        Assert.Equal(
            ["{", "  \"items\": [ … ],", "  \"debug\": true", "}", ""],
            lines);
    }

    [Fact]
    public async Task A_collapsed_container_ending_the_document_leaves_no_empty_row()
    {
        const string text =
            "{\n"
            + "  \"a\": 1\n"
            + "}";
        var snapshot = new TextSnapshot(text);
        var folds = await GetAsync(snapshot);

        Assert.Equal(["{ … }"], Render(snapshot, [.. folds]));
    }

    [Fact]
    public async Task A_closing_bracket_sharing_its_line_keeps_its_own_row()
    {
        const string text =
            "[\n"
            + "  {\n"
            + "    \"a\": 1\n"
            + "  }, {\n"
            + "    \"b\": 2\n"
            + "  }\n"
            + "]\n";
        var snapshot = new TextSnapshot(text);
        var folds = await GetAsync(snapshot);
        var first = folds.Single(fold => fold.Id == "json-object:$/0:0");

        Assert.Equal(" … ", first.Placeholder);
        Assert.Equal(
            ["[", "  { … ", "}, {", "    \"b\": 2", "  }", "]", ""],
            Render(snapshot, first));
        Assert.Equal(
            " … }",
            folds.Single(fold => fold.Id == "json-object:$/1:0").Placeholder);
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
        Assert.Equal(text[1..], snapshot.GetText(fold.Range));
    }

    [Fact]
    public async Task Comments_and_json5_strings_are_not_structure()
    {
        const string text =
            "{\n"
            + "  // fake { [ ] }\n"
            + "  \"double\": \"// /* { [ ] }\",\n"
            + "  'single': '/* { [ ] }',\n"
            + "  bare /* key */: [\n"
            + "    1,\n"
            + "    2\n"
            + "  ], // trailing comment { }\n"
            + "}\n";
        var snapshot = new TextSnapshot(text);

        var folds = await GetAsync(snapshot);

        Assert.Equal(
            ["json-array:$/bare:0", "json-object:$:0"],
            folds.Select(fold => fold.Id));
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
    public async Task An_unclosed_comment_does_not_make_brackets_inside_it_structure()
    {
        var snapshot = new TextSnapshot("{\n  /* } ]\n");

        Assert.Empty(await GetAsync(snapshot));
    }

    [Fact]
    public void Json_language_definition_supplies_the_provider()
    {
        Assert.IsType<JsonFoldingProvider>(BuiltInSyntaxLanguages.Json.FoldingProvider);
    }

    /// <summary>Renders what the editor shows with the given folds collapsed.</summary>
    private static string[] Render(TextSnapshot snapshot, params FoldRange[] folds) =>
        [.. TextProjectionBuilder.Build(snapshot, folds).Lines
            .Select(line => string.Concat(line.Inlines.Select(inline => inline switch
            {
                ProjectedText projected => snapshot.GetText(projected.Source),
                FoldPlaceholder placeholder => placeholder.DisplayText,
                _ => string.Empty
            })))];

    private static async Task<IReadOnlyList<FoldRange>> GetAsync(TextSnapshot snapshot) =>
        await new JsonFoldingProvider().GetFoldsAsync(
            new EditorProviderContext(snapshot, 0, TextSelection.Caret(0)));
}
