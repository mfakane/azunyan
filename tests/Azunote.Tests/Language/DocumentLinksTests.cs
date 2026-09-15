using Azunyan.Core;
using Azunyan.Syntax;
using Xunit;

namespace Azunote.Tests.Language;

public sealed class DocumentLinksTests
{
    [Fact]
    public async Task Plain_text_without_syntax_still_highlights_urls()
    {
        const string text = "memo: https://example.com/page";
        var provider = DocumentLinks.Attach(null);

        var span = Assert.Single(await GetAsync(provider, text));

        Assert.Equal("https://example.com/page", text[span.Range.Start..span.Range.End]);
        Assert.Equal(SyntaxClassifications.Link, span.Classification);
    }

    [Fact]
    public async Task Url_inside_a_comment_keeps_both_classifications()
    {
        const string text = "// https://example.com/page done";
        var provider = DocumentLinks.Attach(BuiltInSyntaxLanguages.CSharp);

        var spans = await GetAsync(provider, text);

        Assert.Equal(
            ["// :comment", "https://example.com/page:link", " done:comment"],
            spans.Select(span => $"{text[span.Range.Start..span.Range.End]}:{span.Classification}"));
    }

    [Theory]
    [InlineData("https://example.com/page")]
    [InlineData("http://example.com")]
    [InlineData("ftp://files.example.com/x.zip")]
    [InlineData("file://server/share/name.txt")]
    [InlineData("mailto:someone@example.com")]
    public void Accepts_navigable_schemes(string text)
    {
        Assert.True(DocumentLinks.TryCreateNavigableUri(text, out var uri));
        Assert.Equal(text, uri.OriginalString);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("example.com/page")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:display")]
    [InlineData(null)]
    public void Rejects_text_that_is_not_a_navigable_url(string? text)
    {
        Assert.False(DocumentLinks.TryCreateNavigableUri(text, out _));
    }

    private static async Task<IReadOnlyList<SyntaxSpan>> GetAsync(
        ISyntaxProvider provider,
        string text) =>
        await provider.GetSyntaxAsync(
            new EditorProviderContext(new TextSnapshot(text), 0, TextSelection.Caret(0)));
}
