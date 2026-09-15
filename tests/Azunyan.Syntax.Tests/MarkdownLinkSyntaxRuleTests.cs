using Azunyan.Core;
using Xunit;

namespace Azunyan.Syntax.Tests;

public sealed class MarkdownLinkSyntaxRuleTests
{
    [Theory]
    [InlineData("see [note](./other.md) here", "./other.md")]
    [InlineData("see [up](../index.md) here", "../index.md")]
    [InlineData("see [same](other.md) here", "other.md")]
    [InlineData("see [anchor](#section) here", "#section")]
    [InlineData("see [absolute](https://example.com/a) here", "https://example.com/a")]
    [InlineData("![alt](./assets/icon.png)", "./assets/icon.png")]
    [InlineData("[titled](./a.md \"A title\")", "./a.md")]
    [InlineData("[angled](<./a file.md>)", "./a file.md")]
    [InlineData("[paren](./a_(b).md)", "./a_(b).md")]
    public async Task Reports_the_destination_of_an_inline_link(string text, string expected)
    {
        var span = Assert.Single(await GetAsync(text));

        Assert.Equal(expected, text[span.Range.Start..span.Range.End]);
        Assert.Equal(SyntaxClassifications.Link, span.Classification);
    }

    [Theory]
    [InlineData("prose ](./a.md) without a label")]
    [InlineData("[empty]()")]
    [InlineData("[unclosed](./a.md")]
    [InlineData("[broken](./a.md\nnext line)")]
    [InlineData("escaped \\[label](./a.md)")]
    public async Task Ignores_text_that_is_not_an_inline_link(string text)
    {
        Assert.Empty(await GetAsync(text));
    }

    [Fact]
    public async Task Reports_every_link_on_a_line()
    {
        const string text = "[a](./a.md) and [b](../b.md).";

        var spans = await GetAsync(text);

        Assert.Equal(
            ["./a.md", "../b.md"],
            spans.Select(span => text[span.Range.Start..span.Range.End]));
    }

    [Fact]
    public async Task Incremental_analysis_matches_a_full_scan()
    {
        var rule = new MarkdownLinkSyntaxRule();
        var previous = new TextSnapshot("[a](./a.md)\n[b](./old.md)\n");
        var current = new TextSnapshot("[a](./a.md)\n[b](./new.md)\n");
        var previousAnalysis = await rule.GetSyntaxAnalysisAsync(
            new EditorProviderContext(previous, 0, TextSelection.Caret(0)));

        var incremental = await rule.GetSyntaxAsync(
            new EditorProviderContext(current, 0, TextSelection.Caret(0)),
            previous,
            new TextChange(
                new TextRange(previous.Text.IndexOf("old", StringComparison.Ordinal), 3),
                "old",
                "new"),
            previousAnalysis);
        var full = await rule.GetSyntaxAnalysisAsync(
            new EditorProviderContext(current, 0, TextSelection.Caret(0)));

        Assert.Equal(full.Spans, incremental.Spans);
    }

    private static async Task<IReadOnlyList<SyntaxSpan>> GetAsync(string text) =>
        await new MarkdownLinkSyntaxRule().GetSyntaxAsync(
            new EditorProviderContext(new TextSnapshot(text), 0, TextSelection.Caret(0)));
}
