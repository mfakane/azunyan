using Azunyan.Core;
using Xunit;

namespace Azunyan.Core.Tests;

public sealed class ProjectionTests
{
    [Fact]
    public void Inline_adornment_preserves_document_positions_and_affinity()
    {
        var snapshot = new TextSnapshot("abc\ndef");
        var projection = TextProjectionBuilder.Build(
            snapshot,
            inlays: new[]
            {
                new InlineAdornment(
                    "hint",
                    DocumentAnchor.Before(1),
                    "inlay",
                    new AdornmentContent("·"))
            });

        Assert.Equal(2, projection.VisualLineCount);
        Assert.Equal(4, projection.Lines[0].VisualLength);
        Assert.Equal(new VisualPosition(0, 1), projection.MapDocumentPosition(DocumentAnchor.Before(1)));
        Assert.Equal(new VisualPosition(0, 2), projection.MapDocumentPosition(DocumentAnchor.After(1)));
        Assert.Equal(DocumentAnchor.Before(1), projection.MapVisualPosition(new VisualPosition(0, 1)));
        Assert.Equal(DocumentAnchor.After(1), projection.MapVisualPosition(new VisualPosition(0, 2)));
    }

    [Fact]
    public void Incremental_plain_projection_reuses_prefix_and_rebases_suffix_lines()
    {
        var document = new Document("aa\nbb\ncc");
        var oldSnapshot = document.Snapshot;
        var previous = TextProjectionBuilder.Build(oldSnapshot);

        var change = document.Insert(4, "X");
        var incremental = TextProjectionBuilder.BuildIncremental(
            oldSnapshot,
            document.Snapshot,
            previous,
            change);

        Assert.Same(previous.Lines[0], incremental.Lines[0]);
        Assert.Equal("aa", document.Snapshot.GetText(incremental.Lines[0].SourceRange));
        Assert.Equal("bXb", document.Snapshot.GetText(incremental.Lines[1].SourceRange));
        Assert.Equal("cc", document.Snapshot.GetText(incremental.Lines[2].SourceRange));
        Assert.Equal(
            new VisualPosition(2, 1),
            incremental.MapDocumentPosition(DocumentAnchor.Before(8)));
    }

    [Fact]
    public void Incremental_plain_projection_rebuilds_lines_around_newline_edits()
    {
        var document = new Document("a\nb\nc");
        var oldSnapshot = document.Snapshot;
        var previous = TextProjectionBuilder.Build(oldSnapshot);

        var change = document.Insert(2, "\n");
        var incremental = TextProjectionBuilder.BuildIncremental(
            oldSnapshot,
            document.Snapshot,
            previous,
            change);

        Assert.Equal(4, incremental.VisualLineCount);
        Assert.Equal(
            new[] { "a", "", "b", "c" },
            incremental.Lines
                .Select(line => document.Snapshot.GetText(line.SourceRange))
                .ToArray());
        Assert.Same(previous.Lines[0], incremental.Lines[0]);
    }

    [Fact]
    public void Folded_ranges_hide_middle_lines_and_map_hidden_positions_to_placeholder()
    {
        var snapshot = new TextSnapshot("a\nb\nc");
        var projection = TextProjectionBuilder.Build(
            snapshot,
            folds: new[] { new FoldRange("body", new TextRange(2, 2)) });

        Assert.Equal(3, snapshot.Lines.LineCount);
        Assert.Equal(3, projection.VisualLineCount);
        Assert.IsType<FoldPlaceholder>(projection.Lines[1].Inlines.Single());
        Assert.Equal(
            new VisualPosition(1, 0),
            projection.MapDocumentPosition(DocumentAnchor.Before(3)));
        Assert.Equal(
            new VisualPosition(1, 1),
            projection.MapDocumentPosition(DocumentAnchor.After(3)));
        Assert.Equal(
            DocumentAnchor.After(4),
            projection.MapVisualPosition(new VisualPosition(1, 1)));
        Assert.Equal(
            new VisualPosition(1, 1),
            projection.MapDocumentPosition(DocumentAnchor.After(2)));
        Assert.Equal(
            new VisualPosition(2, 0),
            projection.MapDocumentPosition(DocumentAnchor.After(4)));
    }

    [Fact]
    public void Overlapping_folds_are_normalized_deterministically()
    {
        var snapshot = new TextSnapshot("0123456789");
        var projection = TextProjectionBuilder.Build(
            snapshot,
            folds: new[]
            {
                new FoldRange("outer", new TextRange(1, 6)),
                new FoldRange("inner", new TextRange(3, 2)),
                new FoldRange("overlap", new TextRange(6, 3))
            });

        var placeholders = projection.Lines
            .SelectMany(line => line.Inlines)
            .OfType<FoldPlaceholder>()
            .ToArray();

        Assert.Equal(new[] { "outer" }, placeholders.Select(item => item.FoldId));
    }

    [Fact]
    public void Adjacent_folds_preserve_end_and_start_affinity()
    {
        var snapshot = new TextSnapshot("abcdefgh");
        var projection = TextProjectionBuilder.Build(
            snapshot,
            folds: new[]
            {
                new FoldRange("first", new TextRange(1, 2)),
                new FoldRange("second", new TextRange(3, 2))
            });

        Assert.Equal(
            new VisualPosition(0, 2),
            projection.MapDocumentPosition(DocumentAnchor.Before(3)));
        Assert.Equal(
            new VisualPosition(0, 3),
            projection.MapDocumentPosition(DocumentAnchor.After(3)));
    }

    [Fact]
    public void Visual_line_height_index_supports_prefix_lookup_and_local_updates()
    {
        var index = new VisualLineHeightIndex(new[] { 10d, 20d, 15d });

        Assert.Equal(45, index.TotalHeight);
        Assert.Equal(0, index.GetOffset(0));
        Assert.Equal(10, index.GetOffset(1));
        Assert.Equal(30, index.GetOffset(2));
        Assert.Equal(0, index.FindLine(0));
        Assert.Equal(0, index.FindLine(9.99));
        Assert.Equal(1, index.FindLine(10));
        Assert.Equal(2, index.FindLine(44.99));
        Assert.Equal(2, index.FindLine(45));

        index.SetHeight(1, 30);

        Assert.Equal(55, index.TotalHeight);
        Assert.Equal(40, index.GetOffset(2));
        Assert.Equal(2, index.FindLine(54.99));
    }

    [Fact]
    public void Visual_line_height_index_splices_rows_without_changing_lookup_semantics()
    {
        var index = VisualLineHeightIndex.CreateUniform(3, 10);

        index.Splice(1, 1, new[] { 20d, 30d });

        Assert.Equal(4, index.Count);
        Assert.Equal(new[] { 10d, 20d, 30d, 10d },
            Enumerable.Range(0, index.Count).Select(index.GetHeight).ToArray());
        Assert.Equal(30, index.GetOffset(2));
        Assert.Equal(2, index.FindLine(31));
    }

    [Fact]
    public void Plain_visual_rows_are_realized_on_demand()
    {
        var projection = TextProjectionBuilder.Build(new TextSnapshot("a\nb\nc"));
        var rows = VisualRowMapBuilder.Build(projection);

        Assert.Equal(3, rows.Rows.Count);
        Assert.Equal(0, rows.Rows[0].VisualRowIndex);
        Assert.Equal(2, rows.Rows[2].VisualRowIndex);
        Assert.Equal(new[] { 1 }, rows.GetTextRowIndices(projection.Lines[1]));
    }
}
