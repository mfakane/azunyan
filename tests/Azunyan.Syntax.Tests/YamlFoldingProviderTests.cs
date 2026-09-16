using Azunyan.Core;
using Xunit;

namespace Azunyan.Syntax.Tests;

public sealed class YamlFoldingProviderTests
{
    [Fact]
    public async Task A_key_folds_the_block_indented_under_it()
    {
        const string text =
            "name: app\n"
            + "server:\n"
            + "  host: localhost\n"
            + "  port: 8080\n"
            + "debug: true\n";
        var snapshot = new TextSnapshot(text);

        var fold = Assert.Single(await GetAsync(snapshot));

        Assert.Equal("yaml-block:/server:0", fold.Id);
        Assert.Equal("\n  host: localhost\n  port: 8080\n", snapshot.GetText(fold.Range));
        Assert.Equal(" …", fold.Placeholder);
    }

    [Fact]
    public async Task A_collapsed_block_leaves_no_empty_row_behind()
    {
        const string text =
            "name: app\n"
            + "server:\n"
            + "  host: localhost\n"
            + "  port: 8080\n"
            + "debug: true\n";
        var snapshot = new TextSnapshot(text);
        var folds = await GetAsync(snapshot);

        var lines = Render(snapshot, [.. folds]);

        Assert.Equal(["name: app", "server: …", "debug: true", ""], lines);
    }

    [Fact]
    public async Task A_collapsed_block_ending_the_document_leaves_no_empty_row()
    {
        var snapshot = new TextSnapshot("server:\n  host: localhost");
        var folds = await GetAsync(snapshot);

        Assert.Equal(["server: …"], Render(snapshot, [.. folds]));
    }

    [Fact]
    public async Task Nested_blocks_and_sequences_each_fold()
    {
        const string text =
            "jobs:\n"
            + "  build:\n"
            + "    steps:\n"
            + "      - name: checkout\n"
            + "        uses: actions/checkout\n"
            + "      - run: dotnet build\n";
        var snapshot = new TextSnapshot(text);

        var folds = await GetAsync(snapshot);

        Assert.Equal(
            [
                "yaml-block:/jobs:0",
                "yaml-block:/jobs/build:0",
                "yaml-block:/jobs/build/steps:0",
                "yaml-block:/jobs/build/steps/[0]:0"
            ],
            folds.Select(fold => fold.Id));
        Assert.Equal(
            "\n        uses: actions/checkout\n",
            snapshot.GetText(folds.Single(fold => fold.Id.EndsWith("[0]:0", StringComparison.Ordinal)).Range));
    }

    [Fact]
    public async Task A_block_that_ends_in_blank_lines_keeps_them_outside_the_fold()
    {
        const string text =
            "server:\n"
            + "  host: localhost\n"
            + "\n"
            + "\n"
            + "debug: true\n";
        var snapshot = new TextSnapshot(text);

        var fold = Assert.Single(await GetAsync(snapshot));

        Assert.Equal("\n  host: localhost\n", snapshot.GetText(fold.Range));
        Assert.Equal(
            ["server: …", "", "", "debug: true", ""],
            Render(snapshot, fold));
    }

    [Fact]
    public async Task A_comment_line_neither_opens_nor_closes_a_block()
    {
        const string text =
            "server:\n"
            + "# a comment at column zero\n"
            + "  host: localhost\n";
        var snapshot = new TextSnapshot(text);

        var fold = Assert.Single(await GetAsync(snapshot));

        Assert.Equal("yaml-block:/server:0", fold.Id);
        Assert.Equal(
            "\n# a comment at column zero\n  host: localhost\n",
            snapshot.GetText(fold.Range));
    }

    [Fact]
    public async Task A_key_with_a_value_on_the_same_line_does_not_fold()
    {
        var snapshot = new TextSnapshot("name: app\ndebug: true\n");

        Assert.Empty(await GetAsync(snapshot));
    }

    [Fact]
    public async Task A_colon_inside_a_value_does_not_become_the_key()
    {
        const string text =
            "url: https://example.test/a\n"
            + "\"quoted: key\":\n"
            + "  value: 1\n";
        var snapshot = new TextSnapshot(text);

        var fold = Assert.Single(await GetAsync(snapshot));

        Assert.Equal("yaml-block:/\"quoted: key\":0", fold.Id);
    }

    [Fact]
    public async Task Repeated_paths_keep_distinct_identities()
    {
        const string text =
            "- name: first\n"
            + "  run: a\n"
            + "- name: second\n"
            + "  run: b\n";
        var snapshot = new TextSnapshot(text);

        var folds = await GetAsync(snapshot);

        Assert.Equal(
            ["yaml-block:/[0]:0", "yaml-block:/[1]:0"],
            folds.Select(fold => fold.Id));
    }

    [Fact]
    public void Yaml_language_definition_supplies_the_provider()
    {
        Assert.IsType<YamlFoldingProvider>(BuiltInSyntaxLanguages.Yaml.FoldingProvider);
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
        await new YamlFoldingProvider().GetFoldsAsync(
            new EditorProviderContext(snapshot, 0, TextSelection.Caret(0)));
}
