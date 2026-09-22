using Azunyan.Core;
using Xunit;

namespace Azunyan.Core.Tests;

public sealed class ProvisionalProviderResultsTests
{
    [Fact]
    public async Task Unaffected_highlighting_is_mapped_to_the_edited_snapshot()
    {
        var oldSnapshot = new TextSnapshot("<a x=\"1\">\n<b />\n</a>");
        using var scheduler = new EditorProviderScheduler(new EditorProviderSet
        {
            Syntax = new FixedSyntaxProvider(new[]
            {
                new SyntaxSpan(new TextRange(1, 1), "keyword"),
                new SyntaxSpan(new TextRange(11, 1), "keyword"),
                new SyntaxSpan(new TextRange(18, 1), "keyword")
            })
        });
        var previous = (await scheduler.RequestDocumentAsync(
            oldSnapshot,
            TextSelection.Caret(0)))!;
        var document = new Document(oldSnapshot.Text);
        var change = document.Insert(10, "\n");

        var provisional = previous.MapUnchangedRanges(document.Snapshot, change);

        Assert.Equal(
            new[] { new TextRange(1, 1), new TextRange(12, 1), new TextRange(19, 1) },
            provisional.Syntax.Select(span => span.Range));
        Assert.False(provisional.FoldsAreComplete);
    }

    private sealed class FixedSyntaxProvider(IReadOnlyList<SyntaxSpan> spans) : ISyntaxProvider
    {
        public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
            EditorProviderContext context,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(spans);
    }
}
