using Azunyan.Core;
using Xunit;

namespace Azunyan.Syntax.Tests;

public sealed class UrlSyntaxRuleTests
{
    [Theory]
    [InlineData("see https://example.com now", "https://example.com")]
    [InlineData("http://example.com/a/b?c=1&d=2#e", "http://example.com/a/b?c=1&d=2#e")]
    [InlineData("ftp://files.example.com/x.zip", "ftp://files.example.com/x.zip")]
    [InlineData("file://server/share/name.txt", "file://server/share/name.txt")]
    [InlineData("mail me at mailto:someone@example.com", "mailto:someone@example.com")]
    public async Task Recognizes_navigable_schemes(string text, string expected)
    {
        var span = Assert.Single(await GetAsync(text));

        Assert.Equal(expected, text[span.Range.Start..span.Range.End]);
        Assert.Equal(SyntaxClassifications.Link, span.Classification);
    }

    [Theory]
    [InlineData("Read https://example.com/page.", "https://example.com/page")]
    [InlineData("Read https://example.com/page, then stop", "https://example.com/page")]
    [InlineData("(https://example.com/page)", "https://example.com/page")]
    [InlineData("[https://example.com/page]", "https://example.com/page")]
    public async Task Stops_before_punctuation_that_ends_the_sentence(
        string text,
        string expected)
    {
        var span = Assert.Single(await GetAsync(text));

        Assert.Equal(expected, text[span.Range.Start..span.Range.End]);
    }

    [Fact]
    public async Task Keeps_a_closing_parenthesis_that_belongs_to_the_url()
    {
        const string text = "See https://ja.wikipedia.org/wiki/Foo_(bar) for more.";

        var span = Assert.Single(await GetAsync(text));

        Assert.Equal(
            "https://ja.wikipedia.org/wiki/Foo_(bar)",
            text[span.Range.Start..span.Range.End]);
    }

    [Fact]
    public async Task Stops_at_japanese_punctuation()
    {
        const string text = "詳しくは https://example.com/ja を参照（https://example.com/b）。";

        var spans = await GetAsync(text);

        Assert.Equal(
            ["https://example.com/ja", "https://example.com/b"],
            spans.Select(span => text[span.Range.Start..span.Range.End]));
    }

    [Theory]
    [InlineData("no scheme here example.com")]
    [InlineData("https://")]
    [InlineData("xhttps://example.com")]
    public async Task Ignores_text_that_is_not_a_url(string text)
    {
        Assert.Empty(await GetAsync(text));
    }

    [Fact]
    public async Task Incremental_analysis_matches_a_full_scan()
    {
        var rule = new UrlSyntaxRule();
        var previous = new TextSnapshot("a https://example.com/1\nb https://example.com/old\n");
        var current = new TextSnapshot("a https://example.com/1\nb https://example.com/new\n");
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
        await new UrlSyntaxRule().GetSyntaxAsync(
            new EditorProviderContext(new TextSnapshot(text), 0, TextSelection.Caret(0)));
}
