using Azunyan.Core;
using Xunit;

namespace Azunote.Tests;

public sealed class ExternalToolPlaceholderCompletionProviderTests
{
    [Fact]
    public void Completes_placeholders_in_arbitrary_text()
    {
        const string text = "--file=${doc";

        var result = ExternalToolPlaceholderCompletionProvider.GetCompletions(
            text,
            text.Length);

        Assert.NotNull(result);
        Assert.Equal(
            new TextRange(text.IndexOf("${", StringComparison.Ordinal) + 2, 3),
            result!.ReplacementRange);
        Assert.Contains(result.Items, item => item.Label == "${document}");
    }

    [Fact]
    public void Filters_the_input_placeholder_as_it_is_typed()
    {
        const string text = "${i";

        var result = ExternalToolPlaceholderCompletionProvider.GetCompletions(
            text,
            text.Length);

        Assert.NotNull(result);
        Assert.Equal(
            ["${input}", "${input:1}", "${input:groupname}"],
            result!.Items.Select(item => item.Label));
        Assert.Equal(
            "input}",
            Assert.Single(result.Items, item => item.Label == "${input}").InsertText);
    }

    [Fact]
    public void Closed_placeholder_replaces_only_the_name()
    {
        const string text = "prefix ${fi} suffix";
        var caretPosition = text.IndexOf('}');

        var result = ExternalToolPlaceholderCompletionProvider.GetCompletions(
            text,
            caretPosition);

        Assert.NotNull(result);
        Assert.Equal(
            TextRange.FromBounds(text.IndexOf("${", StringComparison.Ordinal) + 2, caretPosition),
            result!.ReplacementRange);
        var item = Assert.Single(result.Items, candidate => candidate.Label == "${file}");
        Assert.Equal("file", item.InsertText);
    }

    [Fact]
    public void Does_not_complete_outside_an_open_placeholder()
    {
        Assert.Null(
            ExternalToolPlaceholderCompletionProvider.GetCompletions(
                "ordinary text",
                "ordinary text".Length));
    }
}
