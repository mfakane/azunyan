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

    [Fact]
    public async Task Repeated_requests_use_the_current_prefix_and_snapshot()
    {
        var provider = new DocumentWordCompletionProvider();
        var snapshot = new TextSnapshot("al be alpha beta alpha");
        EditorProviderContext At(TextSnapshot text, int position) =>
            new(text, position, TextSelection.Caret(position));

        var first = await provider.GetCompletionsAsync(At(snapshot, 2));
        Assert.Equal(["alpha"], first!.Items.Select(item => item.Label));
        var moved = await provider.GetCompletionsAsync(At(snapshot, 5));
        Assert.Equal(["beta"], moved!.Items.Select(item => item.Label));
        Assert.Equal(TextRange.FromBounds(3, 5), moved.ReplacementRange);

        var changed = new TextSnapshot("al be alpine berry berry");
        var edited = await provider.GetCompletionsAsync(At(changed, 2));
        Assert.Equal(["alpine"], edited!.Items.Select(item => item.Label));
        var restored = await provider.GetCompletionsAsync(At(snapshot, 2));
        Assert.Equal(["alpha"], restored!.Items.Select(item => item.Label));
    }

    [Fact]
    public async Task Canceled_request_throws_even_after_a_successful_request()
    {
        var provider = new DocumentWordCompletionProvider();
        var context = CreateContext("al alpha", 2);
        Assert.NotNull(await provider.GetCompletionsAsync(context));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await provider.GetCompletionsAsync(context, canceled.Token));
    }

    private static EditorProviderContext CreateContext(string text, int position) =>
        new(new TextSnapshot(text), position, TextSelection.Caret(position));
}
