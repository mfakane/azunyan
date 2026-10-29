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

    [Fact]
    public async Task Inlays_move_with_their_text_and_those_inside_replaced_text_are_dropped()
    {
        var oldSnapshot = new TextSnapshot("run(alpha, beta, gamma)");
        using var scheduler = new EditorProviderScheduler(new EditorProviderSet
        {
            Inlay = new FixedInlayProvider(
            [
                new InlineAdornment("a", DocumentAnchor.Before(4), "hint", new AdornmentContent("x: ")),
                new InlineAdornment("b", DocumentAnchor.Before(13), "hint", new AdornmentContent("y: ")),
                new InlineAdornment("c", DocumentAnchor.Before(17), "hint", new AdornmentContent("z: "))
            ])
        });
        var previous = (await scheduler.RequestViewportAsync(
            oldSnapshot,
            TextRange.FromBounds(0, oldSnapshot.Length),
            TextSelection.Caret(0)))!;
        var document = new Document(oldSnapshot.Text);
        var change = document.Replace(TextRange.FromBounds(11, 15), "b");

        var mapped = previous.MapThroughChange(document.Snapshot, change);

        Assert.Same(document.Snapshot, mapped.Context.Snapshot);
        Assert.Equal(
            [("a", 4), ("c", 14)],
            mapped.Inlays.Select(inlay => (inlay.Id, inlay.Anchor.Position.Offset)));
    }

    private sealed class FixedInlayProvider(IReadOnlyList<InlineAdornment> inlays) : IInlayProvider
    {
        public ValueTask<IReadOnlyList<InlineAdornment>> GetInlaysAsync(
            EditorProviderContext context,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(inlays);
    }

    private sealed class FixedSyntaxProvider(IReadOnlyList<SyntaxSpan> spans) : ISyntaxProvider
    {
        public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
            EditorProviderContext context,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(spans);
    }
}
