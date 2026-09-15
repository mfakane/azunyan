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
        var provider = DocumentLinks.Attach(null, "plain-text");

        var span = Assert.Single(await GetAsync(provider, text));

        Assert.Equal("https://example.com/page", text[span.Range.Start..span.Range.End]);
        Assert.Equal(SyntaxClassifications.Link, span.Classification);
    }

    [Fact]
    public async Task Url_inside_a_comment_keeps_both_classifications()
    {
        const string text = "// https://example.com/page done";
        var provider = DocumentLinks.Attach(BuiltInSyntaxLanguages.CSharp, "csharp");

        var spans = await GetAsync(provider, text);

        Assert.Equal(
            ["// :comment", "https://example.com/page:link", " done:comment"],
            spans.Select(span => $"{text[span.Range.Start..span.Range.End]}:{span.Classification}"));
    }

    [Fact]
    public async Task Markdown_highlights_relative_inline_link_destinations()
    {
        const string text = "See [other](./other.md) and [up](../index.md) and https://example.com.";
        var provider = DocumentLinks.Attach(BuiltInSyntaxLanguages.Markdown, "markdown");

        var spans = await GetAsync(provider, text);

        Assert.Equal(
            ["./other.md", "../index.md", "https://example.com"],
            spans
                .Where(span => span.Classification == SyntaxClassifications.Link)
                .Select(span => text[span.Range.Start..span.Range.End]));
    }

    [Fact]
    public async Task Other_modes_do_not_treat_a_bracket_pair_as_a_link()
    {
        const string text = "var x = map[key](./not-a-link.md);";
        var provider = DocumentLinks.Attach(BuiltInSyntaxLanguages.CSharp, "csharp");

        var spans = await GetAsync(provider, text);

        Assert.DoesNotContain(spans, span => span.Classification == SyntaxClassifications.Link);
    }

    [Theory]
    [InlineData("https://example.com/page")]
    [InlineData("http://example.com")]
    [InlineData("ftp://files.example.com/x.zip")]
    [InlineData("mailto:someone@example.com")]
    public void Absolute_urls_are_opened_by_windows(string text)
    {
        var target = DocumentLinks.Resolve(text, @"C:\notes\index.md", _ => true, _ => false);

        Assert.Equal(DocumentLinkAction.OpenUri, target.Action);
        Assert.Equal(text, target.Uri?.OriginalString);
    }

    [Theory]
    [InlineData("./other.md", @"C:\notes\other.md")]
    [InlineData("../shared/other.md", @"C:\shared\other.md")]
    [InlineData("sub/other.md", @"C:\notes\sub\other.md")]
    [InlineData("other%20file.md", @"C:\notes\other file.md")]
    [InlineData("./other.md#section", @"C:\notes\other.md")]
    public void Relative_paths_resolve_against_the_document_folder(
        string text,
        string expected)
    {
        var target = DocumentLinks.Resolve(
            text,
            @"C:\notes\index.md",
            _ => true,
            path => path == expected);

        Assert.Equal(DocumentLinkAction.OpenInEditor, target.Action);
        Assert.Equal(expected, target.Path);
    }

    [Fact]
    public void A_file_no_language_mode_covers_is_opened_by_windows()
    {
        var target = DocumentLinks.Resolve(
            "./assets/icon.png",
            @"C:\notes\index.md",
            _ => false,
            _ => true);

        Assert.Equal(DocumentLinkAction.OpenWithShell, target.Action);
        Assert.Equal(@"C:\notes\assets\icon.png", target.Path);
    }

    [Fact]
    public void A_missing_file_is_not_opened()
    {
        var target = DocumentLinks.Resolve(
            "./missing.md",
            @"C:\notes\index.md",
            _ => true,
            _ => false);

        Assert.Equal(DocumentLinkAction.None, target.Action);
    }

    [Theory]
    [InlineData("./other.md")]
    [InlineData("other.md")]
    public void A_relative_path_needs_a_saved_document(string text)
    {
        var target = DocumentLinks.Resolve(text, null, _ => true, _ => true);

        Assert.Equal(DocumentLinkAction.None, target.Action);
    }

    [Theory]
    [InlineData("#section")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:display")]
    [InlineData("")]
    [InlineData(null)]
    public void Targets_azunote_cannot_open_are_ignored(string? text)
    {
        var target = DocumentLinks.Resolve(text, @"C:\notes\index.md", _ => true, _ => true);

        Assert.Equal(DocumentLinkAction.None, target.Action);
    }

    private static async Task<IReadOnlyList<SyntaxSpan>> GetAsync(
        ISyntaxProvider provider,
        string text) =>
        await provider.GetSyntaxAsync(
            new EditorProviderContext(new TextSnapshot(text), 0, TextSelection.Caret(0)));
}
