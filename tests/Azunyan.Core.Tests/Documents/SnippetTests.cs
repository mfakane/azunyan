using Azunyan.Core;
using Xunit;

namespace Azunyan.Core.Tests;

public sealed class SnippetTests
{
    [Fact]
    public void Placeholders_insert_their_defaults_and_choices()
    {
        var snippet = Snippet.Parse("Html(${1:value}, ${2|one,two|})$0");

        Assert.Equal("Html(value, one)", snippet.Text);
        Assert.Equal(
            [
                new SnippetPlaceholder(1, TextRange.FromBounds(5, 10)),
                new SnippetPlaceholder(2, TextRange.FromBounds(12, 15)),
                new SnippetPlaceholder(0, new TextRange(16, 0))
            ],
            snippet.Placeholders);
    }

    [Fact]
    public void A_tab_stop_repeats_the_default_of_its_number_and_placeholders_nest()
    {
        var snippet = Snippet.Parse("for (${1:i} = 0; $1 < ${2:${3:list}.Count}; $1++)");

        Assert.Equal("for (i = 0; i < list.Count; i++)", snippet.Text);
        Assert.Equal(
            [
                new SnippetPlaceholder(1, new TextRange(5, 1)),
                new SnippetPlaceholder(1, new TextRange(12, 1)),
                new SnippetPlaceholder(2, TextRange.FromBounds(16, 26)),
                new SnippetPlaceholder(3, TextRange.FromBounds(16, 20), Parent: 2),
                new SnippetPlaceholder(1, new TextRange(28, 1))
            ],
            snippet.Placeholders);
    }

    [Fact]
    public void Escapes_variables_transforms_and_broken_syntax_read_as_text()
    {
        Assert.Equal("$1 } a}b", Snippet.Parse(@"\$1 \} ${1:a\}b}").Text);
        Assert.Equal("x", Snippet.Parse("${TM_SELECTED_TEXT:x}$UNKNOWN").Text);
        Assert.Equal("a", Snippet.Parse("${1/(.*)/${1:/upcase}/}a").Text);
        Assert.Equal("${1:abc", Snippet.Parse("${1:abc").Text);
        Assert.Equal("$ 5$", Snippet.Parse("$ 5$").Text);
    }
}
