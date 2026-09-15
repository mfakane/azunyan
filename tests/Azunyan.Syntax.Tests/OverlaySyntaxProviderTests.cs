using Azunyan.Core;
using Xunit;

namespace Azunyan.Syntax.Tests;

public sealed class OverlaySyntaxProviderTests
{
    [Fact]
    public async Task Overlay_splits_the_base_span_it_covers()
    {
        const string text = "// see https://example.com now";
        var provider = new OverlaySyntaxProvider(
            new LineRemainderSyntaxRule("//", "comment"),
            new UrlSyntaxRule());

        var spans = await GetAsync(provider, text);

        Assert.Equal(
            ["// see :comment", "https://example.com:link", " now:comment"],
            spans.Select(span => $"{text[span.Range.Start..span.Range.End]}:{span.Classification}"));
    }

    [Fact]
    public async Task Overlay_replaces_a_base_span_it_covers_completely()
    {
        const string text = "\"https://example.com\"";
        var provider = new OverlaySyntaxProvider(
            new DelimitedSyntaxRule("\"", "\"", "string", allowLineBreaks: false),
            new UrlSyntaxRule());

        var spans = await GetAsync(provider, text);

        Assert.Equal(
            ["\":string", "https://example.com:link", "\":string"],
            spans.Select(span => $"{text[span.Range.Start..span.Range.End]}:{span.Classification}"));
    }

    [Fact]
    public async Task Base_spans_without_an_overlay_are_preserved()
    {
        const string text = "var x = 1; // plain";
        var provider = new OverlaySyntaxProvider(
            new CompositeSyntaxProvider(
            [
                new LineRemainderSyntaxRule("//", "comment"),
                new KeywordSyntaxRule(["var"])
            ]),
            new UrlSyntaxRule());

        var spans = await GetAsync(provider, text);

        Assert.Equal(
            ["var:keyword", "// plain:comment"],
            spans.Select(span => $"{text[span.Range.Start..span.Range.End]}:{span.Classification}"));
    }

    [Fact]
    public async Task Combined_spans_never_overlap()
    {
        const string text = "# https://example.com/a and https://example.com/b\n";
        var provider = new OverlaySyntaxProvider(
            new LineRemainderSyntaxRule("#", "comment"),
            new UrlSyntaxRule());

        var spans = await GetAsync(provider, text);

        Assert.All(
            spans.Zip(spans.Skip(1)),
            pair => Assert.True(pair.First.Range.End <= pair.Second.Range.Start));
        Assert.Equal(
            2,
            spans.Count(span => span.Classification == SyntaxClassifications.Link));
    }

    [Fact]
    public async Task Incremental_analysis_matches_a_full_scan()
    {
        var provider = new OverlaySyntaxProvider(
            new LineRemainderSyntaxRule("//", "comment"),
            new UrlSyntaxRule());
        var previous = new TextSnapshot("// https://example.com/old\ntext\n");
        var current = new TextSnapshot("// https://example.com/new\ntext\n");
        var previousAnalysis = await provider.GetSyntaxAnalysisAsync(
            new EditorProviderContext(previous, 0, TextSelection.Caret(0)));

        var incremental = await provider.GetSyntaxAsync(
            new EditorProviderContext(current, 0, TextSelection.Caret(0)),
            previous,
            new TextChange(
                new TextRange(previous.Text.IndexOf("old", StringComparison.Ordinal), 3),
                "old",
                "new"),
            previousAnalysis);
        var full = await provider.GetSyntaxAnalysisAsync(
            new EditorProviderContext(current, 0, TextSelection.Caret(0)));

        Assert.Equal(full.Spans, incremental.Spans);
    }

    private static async Task<IReadOnlyList<SyntaxSpan>> GetAsync(
        OverlaySyntaxProvider provider,
        string text) =>
        await provider.GetSyntaxAsync(
            new EditorProviderContext(new TextSnapshot(text), 0, TextSelection.Caret(0)));
}
