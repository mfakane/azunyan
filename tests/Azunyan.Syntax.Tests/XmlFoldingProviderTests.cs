using Azunyan.Core;
using Xunit;

namespace Azunyan.Syntax.Tests;

public sealed class XmlFoldingProviderTests
{
    [Fact]
    public async Task Nested_elements_get_stable_paths_and_ignore_opaque_markup()
    {
        const string text =
            "<!DOCTYPE root [ <!ELEMENT root ANY> ]>\n"
            + "<root>\n"
            + "  <!-- <fake> -->\n"
            + "  <![CDATA[<fake>]]>\n"
            + "  <item>\n"
            + "    <leaf>text</leaf>\n"
            + "  </item>\n"
            + "  <item>\n"
            + "    <child />\n"
            + "  </item>\n"
            + "</root>\n";
        var snapshot = new TextSnapshot(text);

        var folds = await GetAsync(snapshot);

        Assert.Equal(
            [
                "xml-element:$/root[0]",
                "xml-element:$/root[0]/item[0]",
                "xml-element:$/root[0]/item[1]"
            ],
            folds.Select(fold => fold.Id));
        Assert.Equal(
            [" … </root>", " … </item>", " … </item>"],
            folds.Select(fold => fold.Placeholder));
    }

    [Fact]
    public async Task Closing_line_tail_stays_visible_after_a_fold()
    {
        const string text =
            "<root>\n"
            + "  <item>\n"
            + "    value\n"
            + "  </item> trailing\n"
            + "</root>";
        var snapshot = new TextSnapshot(text);

        var item = Assert.Single(
            await GetAsync(snapshot),
            fold => fold.Id == "xml-element:$/root[0]/item[0]");

        Assert.Equal(" … ", item.Placeholder);
        Assert.Equal("\n    value\n  ", snapshot.GetText(item.Range));
    }

    [Fact]
    public async Task Mismatched_or_unclosed_elements_do_not_create_invalid_folds()
    {
        var mismatched = new TextSnapshot(
            "<root>\n"
            + "  <item>\n"
            + "    <bad>\n"
            + "  </root>\n"
            + "</item>\n");
        var unclosed = new TextSnapshot("<root>\n  <item>\n");

        Assert.Empty(await GetAsync(mismatched));
        Assert.Empty(await GetAsync(unclosed));
    }

    [Theory]
    [InlineData("<broken\n<valid>\n  value\n</valid>")]
    [InlineData("</broken\n<valid>\n  value\n</valid>")]
    [InlineData("<broken value=\"unfinished\n<valid>\n  value\n</valid>")]
    public async Task Incomplete_tags_resync_at_the_next_raw_opening_tag(string text)
    {
        var analysis = await GetAnalysisAsync(new TextSnapshot(text));

        Assert.False(analysis.IsComplete);
        Assert.Contains(analysis.Folds, fold => fold.Id == "xml-element:$/valid[0]");
    }

    [Theory]
    [InlineData("<")]
    [InlineData("<!")]
    [InlineData("<@")]
    public async Task Incomplete_markup_openers_report_partial_analysis(string text)
    {
        var analysis = await GetAnalysisAsync(new TextSnapshot(text));

        Assert.False(analysis.IsComplete);
    }

    [Fact]
    public void Xml_language_definition_supplies_the_provider()
    {
        Assert.IsType<XmlFoldingProvider>(BuiltInSyntaxLanguages.Xml.FoldingProvider);
    }

    private static async Task<IReadOnlyList<FoldRange>> GetAsync(TextSnapshot snapshot) =>
        await new XmlFoldingProvider().GetFoldsAsync(
            new EditorProviderContext(snapshot, 0, TextSelection.Caret(0)));

    private static async Task<FoldingAnalysis> GetAnalysisAsync(TextSnapshot snapshot) =>
        await new XmlFoldingProvider().GetFoldingAnalysisAsync(
            new EditorProviderContext(snapshot, 0, TextSelection.Caret(0)));
}
