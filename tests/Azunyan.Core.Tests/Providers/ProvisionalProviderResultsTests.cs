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

    [Fact]
    public async Task Provisional_mapping_keeps_only_the_requested_context()
    {
        var text = string.Join('\n', Enumerable.Range(0, 1_000).Select(index => $"line-{index:D4}"));
        var oldSnapshot = new TextSnapshot(text);
        var spans = Enumerable.Range(0, oldSnapshot.Lines.LineCount)
            .Select(line => new SyntaxSpan(oldSnapshot.Lines.GetLineRange(line), "line"))
            .ToArray();
        using var scheduler = new EditorProviderScheduler(new EditorProviderSet
        {
            Syntax = new FixedSyntaxProvider(spans)
        });
        var previous = (await scheduler.RequestDocumentAsync(oldSnapshot, TextSelection.Caret(0)))!;
        var document = new Document(text);
        var position = document.Snapshot.Lines.GetLineStart(500);
        var change = document.Insert(position, "x");
        var target = TextRange.FromBounds(
            document.Snapshot.Lines.GetLineStart(495),
            document.Snapshot.Lines.GetLineEnd(505));

        var provisional = previous.MapUnchangedRanges(document.Snapshot, change, target);

        Assert.Equal(10, provisional.Syntax.Count);
        Assert.All(provisional.Syntax, span => Assert.True(
            span.Range.Start < target.End && target.Start < span.Range.End));
    }

    private sealed class FixedSyntaxProvider(IReadOnlyList<SyntaxSpan> spans) : ISyntaxProvider
    {
        public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
            EditorProviderContext context,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(spans);
    }
}
