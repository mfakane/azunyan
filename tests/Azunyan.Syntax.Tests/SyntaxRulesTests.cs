using Azunyan.Core;
using Xunit;

namespace Azunyan.Syntax.Tests;

public sealed class SyntaxRulesTests
{
    [Fact]
    public async Task Delimited_rule_handles_multiline_and_unterminated_ranges()
    {
        var rule = new DelimitedSyntaxRule("/*", "*/", "comment");

        var spans = await GetAsync(rule, "a /* one\r\ntwo */ b /* open");

        Assert.Equal(["/* one\r\ntwo */", "/* open"], TextOf(spans, "a /* one\r\ntwo */ b /* open"));
    }

    [Fact]
    public async Task Delimited_rule_stops_at_line_end_and_honors_prefix_escape()
    {
        const string text = "\"a\\\"b\" x\r\n\"open\r\nnext";
        var rule = new DelimitedSyntaxRule(
            "\"",
            "\"",
            "string",
            allowLineBreaks: false,
            escapePrefix: "\\");

        var spans = await GetAsync(rule, text);

        Assert.Equal(["\"a\\\"b\"", "\"open"], TextOf(spans, text));
    }

    [Fact]
    public async Task Delimited_rule_honors_doubled_closing_token()
    {
        const string text = "@\"a\"\"b\"";
        var rule = new DelimitedSyntaxRule("@\"", "\"", "string", escapedEndToken: "\"\"");

        var span = Assert.Single(await GetAsync(rule, text));

        Assert.Equal(text, text[span.Range.Start..span.Range.End]);
    }

    [Fact]
    public async Task Line_remainder_excludes_crlf_and_can_require_line_start()
    {
        const string text = "x# no\r\n# yes\r\n";
        var rule = new LineRemainderSyntaxRule("#", "heading", requireLineStart: true);

        var span = Assert.Single(await GetAsync(rule, text));

        Assert.Equal("# yes", text[span.Range.Start..span.Range.End]);
    }

    [Fact]
    public async Task Keyword_rule_observes_unicode_identifier_boundaries_and_comparison()
    {
        const string text = "TODO TODO2 αTODO TODO_ todo";
        var rule = new KeywordSyntaxRule(["TODO"], comparer: StringComparer.OrdinalIgnoreCase);

        var spans = await GetAsync(rule, text);

        Assert.Equal(["TODO", "todo"], TextOf(spans, text));
    }

    [Fact]
    public async Task Regex_rule_rejects_zero_length_matches()
    {
        var rule = new RegexSyntaxRule("^", "invalid");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await GetAsync(rule, "text"));
    }

    private static async Task<IReadOnlyList<SyntaxSpan>> GetAsync(ISyntaxProvider provider, string text)
    {
        var snapshot = new TextSnapshot(text);
        return await provider.GetSyntaxAsync(
            new EditorProviderContext(snapshot, 0, TextSelection.Caret(0)));
    }

    private static string[] TextOf(IEnumerable<SyntaxSpan> spans, string text) =>
        [.. spans.Select(span => text[span.Range.Start..span.Range.End])];
}
