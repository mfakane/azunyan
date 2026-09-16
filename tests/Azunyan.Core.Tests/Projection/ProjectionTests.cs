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
    public void Incremental_plain_projection_maps_all_lines_inserted_into_empty_document()
    {
        var document = new Document();
        var oldSnapshot = document.Snapshot;
        var previous = TextProjectionBuilder.Build(oldSnapshot);

        var change = document.Insert(0, "first\nsecond\nthird");
        var incremental = TextProjectionBuilder.BuildIncremental(
            oldSnapshot,
            document.Snapshot,
            previous,
            change);

        Assert.Equal(3, incremental.VisualLineCount);
        Assert.Equal(
            new VisualPosition(2, 5),
            incremental.MapDocumentPosition(DocumentAnchor.Before(document.Length)));
    }

    [Fact]
    public void Incremental_decorated_projection_rebases_unaffected_folds_and_inlays()
    {
        var document = new Document("zz\naa\nbb\ncc\ndd\nee\n");
        var oldSnapshot = document.Snapshot;
        var oldFold = new FoldRange("body", new TextRange(9, 5), "...");
        var oldInlay = new InlineAdornment(
            "type",
            DocumentAnchor.Before(15),
            "hint",
            new AdornmentContent(" : str"));
        var previous = TextProjectionBuilder.Build(
            oldSnapshot,
            folds: new[] { oldFold },
            inlays: new[] { oldInlay });

        var change = document.Insert(4, "X");
        var currentFold = new FoldRange("body", new TextRange(10, 5), "...");
        var currentInlay = new InlineAdornment(
            "type",
            DocumentAnchor.Before(16),
            "hint",
            new AdornmentContent(" : str"));
        var incremental = TextProjectionBuilder.BuildIncremental(
            oldSnapshot,
            document.Snapshot,
            previous,
            change,
            folds: new[] { currentFold },
            inlays: new[] { currentInlay });
        var expected = TextProjectionBuilder.Build(
            document.Snapshot,
            folds: new[] { currentFold },
            inlays: new[] { currentInlay });

        AssertProjectionEquivalent(expected, incremental, document.Snapshot);
        Assert.Same(previous.Lines[0], incremental.Lines[0]);
    }

    [Fact]
    public void Incremental_projection_matches_full_projection_across_seeded_random_edits()
    {
        var document = new Document(string.Join('\n', Enumerable.Range(0, 24).Select(index => $"line-{index:D2}")));
        var previous = TextProjectionBuilder.Build(document.Snapshot);
        var random = new Random(20260829);
        var history = new List<string>();

        for (var iteration = 0; iteration < 80; iteration++)
        {
            var oldSnapshot = document.Snapshot;
            var start = random.Next(oldSnapshot.Length + 1);
            var maxLength = Math.Min(5, oldSnapshot.Length - start);
            var length = random.Next(maxLength + 1);
            var inserted = new[] { "", "x", "YZ", "\n", "q\nrs" }[random.Next(5)];
            var operation = $"iteration={iteration}, range={start}:{length}, old={oldSnapshot.GetText(new TextRange(start, length))}, inserted={inserted.Replace("\n", "\\n", StringComparison.Ordinal)}";
            history.Add(operation);
            var change = document.Replace(new TextRange(start, length), inserted);

            var incremental = TextProjectionBuilder.BuildIncremental(
                oldSnapshot,
                document.Snapshot,
                previous,
                change);
            var expected = TextProjectionBuilder.Build(document.Snapshot);

            Assert.True(
                expected.VisualLineCount == incremental.VisualLineCount,
                $"{string.Join("; ", history)}, expected={expected.VisualLineCount}, actual={incremental.VisualLineCount}");
            AssertProjectionEquivalent(expected, incremental, document.Snapshot);
            previous = incremental;
        }
    }

    [Fact]
    public void Incremental_decorated_projection_matches_full_projection_when_adornments_change()
    {
        var document = new Document("aa\nbb\ncc\ndd\nee\n");
        var oldSnapshot = document.Snapshot;
        var oldFolds = new[] { new FoldRange("body", new TextRange(3, 2), "...") };
        var oldInlays = new[]
        {
            new InlineAdornment(
                "hint",
                DocumentAnchor.Before(1),
                "type",
                new AdornmentContent(": int"))
        };
        var previous = TextProjectionBuilder.Build(oldSnapshot, oldFolds, oldInlays);

        var change = document.Insert(0, "X");
        var currentFolds = new[] { new FoldRange("body", new TextRange(4, 3), "[fold]") };
        var currentInlays = new[]
        {
            new InlineAdornment(
                "hint",
                DocumentAnchor.After(2),
                "type-hint",
                new AdornmentContent(" : long"))
        };
        var incremental = TextProjectionBuilder.BuildIncremental(
            oldSnapshot,
            document.Snapshot,
            previous,
            change,
            currentFolds,
            currentInlays);
        var expected = TextProjectionBuilder.Build(
            document.Snapshot,
            currentFolds,
            currentInlays);

        AssertProjectionEquivalent(expected, incremental, document.Snapshot);
    }

    [Fact]
    public void Incremental_visual_rows_match_full_rows_across_seeded_random_edits()
    {
        var document = new Document(string.Join('\n', Enumerable.Range(0, 24).Select(index => $"line-{index:D2}")));
        var previousProjection = TextProjectionBuilder.Build(document.Snapshot);
        var previousRows = VisualRowMapBuilder.Build(previousProjection, wrapColumns: 3);
        var random = new Random(20260830);
        var history = new List<string>();

        for (var iteration = 0; iteration < 80; iteration++)
        {
            var oldSnapshot = document.Snapshot;
            var start = random.Next(oldSnapshot.Length + 1);
            var maxLength = Math.Min(5, oldSnapshot.Length - start);
            var length = random.Next(maxLength + 1);
            var inserted = new[] { "", "x", "YZ", "\n", "q\nrs" }[random.Next(5)];
            history.Add(
                $"iteration={iteration}, range={start}:{length}, oldLine={oldSnapshot.Lines.GetLine(start)}, old={oldSnapshot.GetText(new TextRange(start, length))}, inserted={inserted.Replace("\n", "\\n", StringComparison.Ordinal)}");
            var change = document.Replace(new TextRange(start, length), inserted);

            var projection = TextProjectionBuilder.BuildIncremental(
                oldSnapshot,
                document.Snapshot,
                previousProjection,
                change);
            var fullProjection = TextProjectionBuilder.Build(document.Snapshot);
            Assert.True(
                fullProjection.VisualLineCount == projection.VisualLineCount,
                $"{string.Join("; ", history)}, expected={fullProjection.VisualLineCount}, actual={projection.VisualLineCount}");
            AssertProjectionEquivalent(
                fullProjection,
                projection,
                document.Snapshot);
            var incremental = VisualRowMapBuilder.BuildIncremental(
                previousProjection,
                projection,
                previousRows,
                wrapColumns: 3,
                change: change);
            var expected = VisualRowMapBuilder.Build(projection, wrapColumns: 3);

            var expectedRows = expected.Rows.Select(DescribeRow).ToArray();
            var actualRows = incremental.Rows.Select(DescribeRow).ToArray();
            Assert.True(
                expectedRows.SequenceEqual(actualRows),
                $"{string.Join("; ", history)}, rowWindow={incremental.ChangeWindow}, expected={string.Join(",", expectedRows)}, actual={string.Join(",", actualRows)}");
            previousProjection = projection;
            previousRows = incremental;
        }
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
    public void Incremental_projection_stays_aligned_when_a_fold_hides_whole_lines()
    {
        var document = new Document("aa\nbb\ncc\ndd\nee\n");
        var oldSnapshot = document.Snapshot;
        var folds = new[] { new FoldRange("body", TextRange.FromBounds(2, 11), " …") };
        var previous = TextProjectionBuilder.Build(oldSnapshot, folds);

        var change = document.Insert(13, "X");
        var incremental = TextProjectionBuilder.BuildIncremental(
            oldSnapshot,
            document.Snapshot,
            previous,
            change,
            folds,
            inlays: null);
        var expected = TextProjectionBuilder.Build(document.Snapshot, folds);

        Assert.Equal(3, previous.VisualLineCount);
        AssertProjectionEquivalent(expected, incremental, document.Snapshot);
    }

    [Fact]
    public void A_fold_covering_a_line_to_its_end_hides_that_line()
    {
        var snapshot = new TextSnapshot("a\nb\nc");
        var projection = TextProjectionBuilder.Build(
            snapshot,
            folds: new[] { new FoldRange("body", TextRange.FromBounds(1, 5), " …") });

        Assert.Equal(3, snapshot.Lines.LineCount);
        Assert.Equal(1, projection.VisualLineCount);
        Assert.Equal(
            new VisualPosition(0, 1),
            projection.MapDocumentPosition(DocumentAnchor.Before(3)));
        Assert.Equal(
            new VisualPosition(0, 3),
            projection.MapDocumentPosition(DocumentAnchor.After(5)));
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

    private static void AssertProjectionEquivalent(
        TextProjection expected,
        TextProjection actual,
        TextSnapshot snapshot)
    {
        Assert.Equal(expected.VisualLineCount, actual.VisualLineCount);
        for (var lineIndex = 0; lineIndex < expected.VisualLineCount; lineIndex++)
        {
            var expectedLine = expected.Lines[lineIndex];
            var actualLine = actual.Lines[lineIndex];
            Assert.Equal(expectedLine.LogicalLine, actualLine.LogicalLine);
            Assert.Equal(expectedLine.SourceRange, actualLine.SourceRange);
            Assert.Equal(expectedLine.VisualLength, actualLine.VisualLength);
            Assert.Equal(expectedLine.Inlines.Count, actualLine.Inlines.Count);
            for (var inlineIndex = 0; inlineIndex < expectedLine.Inlines.Count; inlineIndex++)
            {
                Assert.Equal(
                    DescribeInline(expectedLine.Inlines[inlineIndex]),
                    DescribeInline(actualLine.Inlines[inlineIndex]));
            }
        }

        for (var offset = 0; offset <= snapshot.Length; offset++)
        {
            foreach (var affinity in new[] { AnchorAffinity.Before, AnchorAffinity.After })
            {
                var anchor = new DocumentAnchor(new DocumentPosition(offset), affinity);
                Assert.Equal(expected.MapDocumentPosition(anchor), actual.MapDocumentPosition(anchor));
                Assert.Equal(expected.IsHidden(anchor), actual.IsHidden(anchor));
            }
        }
    }

    private static string DescribeInline(ProjectionInline inline) => inline switch
    {
        ProjectedText text => $"text:{text.Source}",
        FoldPlaceholder fold => $"fold:{fold.FoldId}:{fold.HiddenSource}:{fold.DisplayText}",
        InlineAdornment inlay =>
            $"inlay:{inlay.Id}:{inlay.Anchor}:{inlay.Kind}:{inlay.Content.Text}",
        _ => throw new ArgumentOutOfRangeException(nameof(inline))
    };

    private static string DescribeRow(VisualRow row) =>
        row.Kind == VisualRowKind.Text
            ? $"text:{row.LogicalLine}:{row.TextStartColumn}:{row.TextLength}"
            : $"block:{row.BlockAdornment!.Id}:{row.LogicalLine}";
}
