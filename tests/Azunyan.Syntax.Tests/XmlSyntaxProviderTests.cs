using Azunyan.Core;
using Xunit;

namespace Azunyan.Syntax.Tests;

public sealed class XmlSyntaxProviderTests
{
    [Fact]
    public async Task Classifies_xml_constructs_and_ignores_markup_inside_opaque_regions()
    {
        const string text =
            "<?xml version=\"1.0\"?>\n"
            + "<root id='1' xmlns:x=\"urn:x\">\n"
            + "  <x:item data=\"a > b\"><![CDATA[<fake/>]]><!-- <fake/> -->text</x:item>\n"
            + "</root>";

        var spans = await GetAsync(text);

        Assert.Equal(
            [
                "<?xml version=\"1.0\"?>:keyword",
                "<:keyword",
                "root:keyword",
                "id:variable",
                "'1':string",
                "xmlns:x:variable",
                "\"urn:x\":string",
                ">:keyword",
                "<:keyword",
                "x:item:keyword",
                "data:variable",
                "\"a > b\":string",
                ">:keyword",
                "<![CDATA[<fake/>]]>:code",
                "<!-- <fake/> -->:comment",
                "</:keyword",
                "x:item:keyword",
                ">:keyword",
                "</:keyword",
                "root:keyword",
                ">:keyword"
            ],
            spans.Select(span => $"{text[span.Range.Start..span.Range.End]}:{span.Classification}"));
    }

    [Fact]
    public async Task Treats_doctype_internal_subsets_and_self_closing_tags_as_opaque_or_tags()
    {
        const string text =
            "<!DOCTYPE root [ <!ELEMENT root ANY> ]>\n"
            + "<?build value=\">\"?>\n"
            + "<root />";

        var spans = await GetAsync(text);

        Assert.Equal(
            [
                "<!DOCTYPE root [ <!ELEMENT root ANY> ]>:keyword",
                "<?build value=\">\"?>:keyword",
                "<:keyword",
                "root:keyword",
                "/>:keyword"
            ],
            spans.Select(span => $"{text[span.Range.Start..span.Range.End]}:{span.Classification}"));
    }

    [Fact]
    public async Task Unterminated_constructs_do_not_throw_or_scan_nested_markup()
    {
        const string text = "<root>\n  <!-- <fake>\n";

        var spans = await GetAsync(text);

        Assert.Equal(
            ["<:keyword", "root:keyword", ">:keyword", "<!-- <fake>\n:comment"],
            spans.Select(span => $"{text[span.Range.Start..span.Range.End]}:{span.Classification}"));
    }

    private static async Task<IReadOnlyList<SyntaxSpan>> GetAsync(string text) =>
        await new XmlSyntaxProvider().GetSyntaxAsync(
            new EditorProviderContext(new TextSnapshot(text), 0, TextSelection.Caret(0)));
}
