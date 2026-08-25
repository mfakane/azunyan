using Azunyan.Core;
using Xunit;

namespace Azunyan.Syntax.Tests;

public sealed class CompositeSyntaxProviderTests
{
    [Fact]
    public async Task Lexical_scan_keeps_comment_markers_in_strings_and_strings_in_comments()
    {
        const string text = "var s = \"http://x\"; // \"comment\"";
        var provider = new CompositeSyntaxProvider(
        [
            new LineRemainderSyntaxRule("//", "comment"),
            new DelimitedSyntaxRule("\"", "\"", "string", allowLineBreaks: false, escapePrefix: "\\"),
            new KeywordSyntaxRule(["var"])
        ]);

        var spans = await GetAsync(provider, text);

        Assert.Equal(
            ["var:keyword", "\"http://x\":string", "// \"comment\":comment"],
            spans.Select(span => $"{text[span.Range.Start..span.Range.End]}:{span.Classification}"));
    }

    [Fact]
    public async Task First_source_wins_at_the_same_position()
    {
        var provider = new CompositeSyntaxProvider(
        [
            new LiteralSyntaxRule("abc", "first"),
            new LiteralSyntaxRule("ab", "second")
        ]);

        var span = Assert.Single(await GetAsync(provider, "abc"));

        Assert.Equal("first", span.Classification);
        Assert.Equal(new TextRange(0, 3), span.Range);
    }

    [Fact]
    public async Task Arbitrary_provider_can_be_interleaved_with_rules()
    {
        var provider = new CompositeSyntaxProvider(
        [
            new DelegateProvider(_ => [new SyntaxSpan(new TextRange(0, 3), "semantic")]),
            new KeywordSyntaxRule(["var"])
        ]);

        var span = Assert.Single(await GetAsync(provider, "var"));

        Assert.Equal("semantic", span.Classification);
    }

    [Fact]
    public async Task Invalid_empty_and_throwing_sources_do_not_discard_other_results()
    {
        var provider = new CompositeSyntaxProvider(
        [
            new ThrowingProvider(),
            new DelegateProvider(_ =>
            [
                new SyntaxSpan(TextRange.Empty(0), "empty"),
                new SyntaxSpan(new TextRange(20, 2), "invalid")
            ]),
            new KeywordSyntaxRule(["ok"])
        ]);

        var span = Assert.Single(await GetAsync(provider, "ok"));

        Assert.Equal("keyword", span.Classification);
    }

    [Fact]
    public async Task Cancellation_is_propagated()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var provider = new CompositeSyntaxProvider(
        [
            new KeywordSyntaxRule(["value"])
        ]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await GetAsync(provider, "value", cancellation.Token));
    }

    private static async Task<IReadOnlyList<SyntaxSpan>> GetAsync(
        ISyntaxProvider provider,
        string text,
        CancellationToken cancellationToken = default)
    {
        var snapshot = new TextSnapshot(text);
        return await provider.GetSyntaxAsync(
            new EditorProviderContext(snapshot, 0, TextSelection.Caret(0)),
            cancellationToken);
    }

    private sealed class DelegateProvider : ISyntaxProvider
    {
        private readonly Func<EditorProviderContext, IReadOnlyList<SyntaxSpan>> _getSpans;

        public DelegateProvider(Func<EditorProviderContext, IReadOnlyList<SyntaxSpan>> getSpans) =>
            _getSpans = getSpans;

        public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
            EditorProviderContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_getSpans(context));
    }

    private sealed class ThrowingProvider : ISyntaxProvider
    {
        public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
            EditorProviderContext context,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Expected test failure.");
    }
}
