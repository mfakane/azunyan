using Xunit;

namespace Azunyan.Core.Tests.Providers;

public sealed class DocumentWordCompletionProviderTests
{
    [Fact]
    public async Task Completes_matching_document_words_and_replaces_the_current_prefix()
    {
        const string text = "alpha alpine alpha\nal";
        var provider = new DocumentWordCompletionProvider();
        var result = await provider.GetCompletionsAsync(CreateContext(text, text.Length));

        Assert.NotNull(result);
        Assert.Equal(TextRange.FromBounds(19, 21), result!.ReplacementRange);
        Assert.Equal(["alpha", "alpine"], result.Items.Select(item => item.Label));
    }

    [Fact]
    public async Task Deduplicates_words_case_insensitively_and_prefers_exact_case()
    {
        const string text = "Alpha alpha ALPACA\nAl";
        var provider = new DocumentWordCompletionProvider();
        var result = await provider.GetCompletionsAsync(CreateContext(text, text.Length));

        Assert.NotNull(result);
        Assert.Equal(["Alpha", "ALPACA"], result!.Items.Select(item => item.Label));
    }

    [Fact]
    public async Task Returns_no_completion_without_a_word_prefix_or_match()
    {
        var provider = new DocumentWordCompletionProvider();

        Assert.Null(await provider.GetCompletionsAsync(CreateContext("alpha ", 6)));
        Assert.Null(await provider.GetCompletionsAsync(CreateContext("alpha\nz", 7)));
        Assert.Null(await provider.GetCompletionsAsync(CreateContext("alpha", 5)));
    }

    private static EditorProviderContext CreateContext(string text, int position) =>
        new(new TextSnapshot(text), position, TextSelection.Caret(position));
}
