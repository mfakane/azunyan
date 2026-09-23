using Azunyan.Core;
using Azunyan.Layout;
using Xunit;

namespace Azunyan.Core.Tests;

public sealed class LayoutTests
{
    [Fact]
    public void Monospace_layout_splits_text_runs_at_syntax_boundaries()
    {
        var snapshot = new TextSnapshot("TODO note");
        var projection = TextProjectionBuilder.Build(snapshot);
        var line = projection.Lines[0];
        var engine = new MonospaceLineLayoutEngine();

        var layout = engine.Layout(
            snapshot,
            line,
            new[] { new SyntaxSpan(new TextRange(0, 4), "keyword") },
            new LayoutMetrics(8, 18, 14));

        Assert.Equal(2, layout.Runs.Count);
        Assert.Equal("TODO", layout.Runs[0].Text);
        Assert.Equal("keyword", layout.Runs[0].Classification);
        Assert.Equal(" note", layout.Runs[1].Text);
        Assert.Null(layout.Runs[1].Classification);
        Assert.Equal(72, layout.Width);
        Assert.Equal(DocumentAnchor.After(4), layout.GetAnchorAtCaretStop(4));
    }

    [Fact]
    public void Syntax_index_preserves_provider_priority_for_unsorted_overlapping_spans()
    {
        var snapshot = new TextSnapshot("zero\none\ntwo");
        var projection = TextProjectionBuilder.Build(snapshot);
        var syntax = new[]
        {
            new SyntaxSpan(new TextRange(5, 3), "first"),
            new SyntaxSpan(new TextRange(0, snapshot.Length), "fallback"),
            new SyntaxSpan(new TextRange(6, 1), "second")
        };
        var engine = new MonospaceLineLayoutEngine();

        var layout = engine.Layout(
            snapshot,
            projection.Lines[1],
            syntax,
            new LayoutMetrics(8, 18, 14));

        Assert.All(layout.Runs, run => Assert.Equal("first", run.Classification));
    }

    [Fact]
    public void Layout_caret_stops_map_back_to_document_anchors_through_inlays()
    {
        var snapshot = new TextSnapshot("ab");
        var projection = TextProjectionBuilder.Build(
            snapshot,
            inlays: new[]
            {
                new InlineAdornment(
                    "hint",
                    DocumentAnchor.Before(1),
                    "hint",
                    new AdornmentContent("::"))
            });
        var layout = new MonospaceLineLayoutEngine().Layout(
            snapshot,
            projection.Lines[0],
            Array.Empty<SyntaxSpan>(),
            new LayoutMetrics(8, 18, 14));

        Assert.Equal(DocumentAnchor.Before(0), layout.GetDocumentAnchorAtCaretStop(0));
        Assert.Equal(DocumentAnchor.Before(1), layout.GetDocumentAnchorAtCaretStop(1));
        Assert.Equal(DocumentAnchor.After(1), layout.GetDocumentAnchorAtCaretStop(2));
        Assert.Equal(DocumentAnchor.After(1), layout.GetDocumentAnchorAtCaretStop(3));
        Assert.Equal(DocumentAnchor.After(2), layout.GetDocumentAnchorAtCaretStop(4));
    }

    [Fact]
    public void Layout_caret_stops_keep_mixed_script_document_offsets_stable()
    {
        const string text = "abc אבג";
        var snapshot = new TextSnapshot(text);
        var projection = TextProjectionBuilder.Build(snapshot);
        var layout = new MonospaceLineLayoutEngine().Layout(
            snapshot,
            projection.Lines[0],
            Array.Empty<SyntaxSpan>(),
            new LayoutMetrics(8, 18, 14));

        Assert.Equal(
            Enumerable.Range(0, text.Length)
                .Select(DocumentAnchor.Before)
                .Append(DocumentAnchor.After(text.Length))
                .ToArray(),
            Enumerable.Range(0, text.Length + 1)
                .Select(layout.GetDocumentAnchorAtCaretStop)
                .ToArray());
    }

    [Fact]
    public void Wrapped_visual_rows_slice_one_projected_line_into_continuation_layouts()
    {
        var snapshot = new TextSnapshot("abcdef");
        var projection = TextProjectionBuilder.Build(snapshot);
        var rows = VisualRowMapBuilder.Build(projection, wrapColumns: 3);
        var heights = new VisualLineHeightIndex(new[] { 18d, 18d });

        Assert.Equal(new[] { 0, 3 }, rows.Rows.Select(row => row.TextStartColumn));
        Assert.Equal(new[] { false, true }, rows.Rows.Select(row => row.IsContinuation));

        var layouts = ViewportLayoutEngine.LayoutVisibleRows(
            snapshot,
            rows,
            heights,
            new LayoutViewport(0, 36),
            overscan: 0,
            Array.Empty<SyntaxSpan>(),
            new LayoutMetrics(8, 18, 14),
            new MonospaceLineLayoutEngine());

        Assert.Equal(new[] { "abc", "def" }, layouts
            .Select(layout => string.Concat(layout.TextLayout!.Runs.Select(run => run.Text))));
        Assert.Equal(24, layouts[1].TextLayout!.Width);
        Assert.Equal(DocumentAnchor.After(4), layouts[1].TextLayout!.GetAnchorAtCaretStop(1));
    }

    [Fact]
    public void Incremental_visual_rows_rebase_wrapped_suffix_and_blocks()
    {
        var document = new Document("aa\nbbbb\ncc\n");
        var oldSnapshot = document.Snapshot;
        var oldProjection = TextProjectionBuilder.Build(oldSnapshot);
        var oldBlock = new BlockAdornment(
            "lens",
            DocumentAnchor.Before(8),
            12,
            "codelens",
            new AdornmentContent("details"));
        var previous = VisualRowMapBuilder.Build(
            oldProjection,
            new[] { oldBlock },
            wrapColumns: 2);

        var change = document.Insert(4, "X");
        var currentProjection = TextProjectionBuilder.BuildIncremental(
            oldSnapshot,
            document.Snapshot,
            oldProjection,
            change);
        var currentBlock = new BlockAdornment(
            "lens",
            DocumentAnchor.Before(9),
            12,
            "codelens",
            new AdornmentContent("details"));
        var incremental = VisualRowMapBuilder.BuildIncremental(
            oldProjection,
            currentProjection,
            previous,
            new[] { currentBlock },
            wrapColumns: 2,
            change: change);
        var expected = VisualRowMapBuilder.Build(
            currentProjection,
            new[] { currentBlock },
            wrapColumns: 2);

        Assert.Equal(
            expected.Rows.Select(DescribeRow),
            incremental.Rows.Select(DescribeRow));
        Assert.Equal(
            expected.GetTextRowIndices(expected.Projection.Lines[2]),
            incremental.GetTextRowIndices(currentProjection.Lines[2]));
        Assert.Equal(
            expected.GetBlockRowIndices(currentBlock.Anchor),
            incremental.GetBlockRowIndices(currentBlock.Anchor));
        Assert.Same(previous.Rows[0], incremental.Rows[0]);
    }

    [Fact]
    public void Incremental_visual_rows_handle_same_snapshot_fold_changes_with_wrapping()
    {
        var snapshot = new TextSnapshot("abcdef\nghijkl\nmnopqr\nstuvwx");
        var previousProjection = TextProjectionBuilder.Build(snapshot);
        var previous = VisualRowMapBuilder.Build(previousProjection, wrapColumns: 3);
        var folds = new[] { new FoldRange("middle", TextRange.FromBounds(7, 9), "...") };
        var projection = TextProjectionBuilder.BuildIncremental(snapshot, previousProjection, folds, null);

        var incremental = VisualRowMapBuilder.BuildIncremental(
            previousProjection,
            projection,
            previous,
            wrapColumns: 3);
        var expected = VisualRowMapBuilder.Build(projection, wrapColumns: 3);

        Assert.Equal(expected.Rows.Select(DescribeRow), incremental.Rows.Select(DescribeRow));
        Assert.NotNull(incremental.ChangeWindow);
        Assert.Same(previous.Rows[0], incremental.Rows[0]);
    }

    [Fact]
    public void Visible_layouts_reuse_cached_unwrapped_line_layouts()
    {
        var snapshot = new TextSnapshot("abcdef");
        var projection = TextProjectionBuilder.Build(snapshot);
        var rows = VisualRowMapBuilder.Build(projection);
        var heights = new VisualLineHeightIndex(new[] { 18d });
        var cache = new Dictionary<ProjectedLine, UnwrappedLineLayout>();
        var metrics = new LayoutMetrics(8, 18, 14);

        var first = ViewportLayoutEngine.LayoutVisibleRows(
            snapshot,
            rows,
            heights,
            new LayoutViewport(0, 18),
            overscan: 0,
            Array.Empty<SyntaxSpan>(),
            metrics,
            new MonospaceLineLayoutEngine(),
            cache);
        var second = ViewportLayoutEngine.LayoutVisibleRows(
            snapshot,
            rows,
            heights,
            new LayoutViewport(0, 18),
            overscan: 0,
            Array.Empty<SyntaxSpan>(),
            metrics,
            new MonospaceLineLayoutEngine(),
            cache);

        Assert.Same(first[0].TextLayout, second[0].TextLayout);
    }

    [Fact]
    public void Measured_wrap_breaks_override_the_fixed_column_fallback()
    {
        var snapshot = new TextSnapshot("abcdef");
        var projection = TextProjectionBuilder.Build(snapshot);
        var rows = VisualRowMapBuilder.Build(
            projection,
            wrapColumns: 99,
            wrappedLineBreaks: new[] { (IReadOnlyList<int>?)new[] { 2, 5 } });

        Assert.Equal(new[] { 0, 2, 5 }, rows.Rows.Select(row => row.TextStartColumn));
        Assert.Equal(new[] { 2, 3, 1 }, rows.Rows.Select(row => row.TextLength));
    }

    [Fact]
    public void Measured_wrap_break_dictionary_leaves_unmeasured_lines_on_the_fallback()
    {
        var snapshot = new TextSnapshot("abcdef\nxyz");
        var projection = TextProjectionBuilder.Build(snapshot);
        var rows = VisualRowMapBuilder.Build(
            projection,
            wrapColumns: 2,
            wrappedLineBreaksByVisualLine: new Dictionary<int, IReadOnlyList<int>>
            {
                [0] = new[] { 3 }
            });

        Assert.Equal(
            new[] { (0, 0, 3), (0, 3, 3), (1, 0, 2), (1, 2, 1) },
            rows.Rows.Select(row => (row.LogicalLine, row.TextStartColumn, row.TextLength)));
    }

    [Fact]
    public void Incremental_visual_rows_rebases_measured_wrap_breaks_by_source_line()
    {
        var document = new Document("aa\nabcdef\nxyz\n");
        var oldSnapshot = document.Snapshot;
        var oldProjection = TextProjectionBuilder.Build(oldSnapshot);
        var oldBreaks = new Dictionary<int, IReadOnlyList<int>>
        {
            [1] = new[] { 2, 5 },
            [2] = new[] { 1 }
        };
        var previous = VisualRowMapBuilder.Build(
            oldProjection,
            wrapColumns: 99,
            wrappedLineBreaksByVisualLine: oldBreaks);

        var change = document.Insert(1, "Z");
        var currentProjection = TextProjectionBuilder.BuildIncremental(
            oldSnapshot,
            document.Snapshot,
            oldProjection,
            change);
        var currentBreaks = new Dictionary<int, IReadOnlyList<int>>
        {
            [1] = new[] { 2, 5 },
            [2] = new[] { 1 }
        };
        var incremental = VisualRowMapBuilder.BuildIncremental(
            oldProjection,
            currentProjection,
            previous,
            wrapColumns: 99,
            wrappedLineBreaksByVisualLine: currentBreaks,
            change: change);
        var expected = VisualRowMapBuilder.Build(
            currentProjection,
            wrapColumns: 99,
            wrappedLineBreaksByVisualLine: currentBreaks);

        Assert.Equal(expected.Rows.Select(DescribeRow), incremental.Rows.Select(DescribeRow));
        Assert.Equal(7, incremental.Rows.Count);
    }

    [Fact]
    public void Large_mixed_script_document_realizes_only_the_viewport_rows()
    {
        var line = "日本語🙂e\u0301 " + new string('x', 92);
        var text = string.Join('\n', Enumerable.Repeat(line, 100_000));
        Assert.True(text.Length >= 10_000_000);

        var snapshot = new TextSnapshot(text);
        var projection = TextProjectionBuilder.Build(snapshot);
        var rows = VisualRowMapBuilder.Build(projection);
        var heights = new VisualLineHeightIndex(
            Enumerable.Repeat(18d, rows.Rows.Count));

        var layouts = ViewportLayoutEngine.LayoutVisibleRows(
            snapshot,
            rows,
            heights,
            new LayoutViewport(900_000, 72),
            overscan: 18,
            Array.Empty<SyntaxSpan>(),
            new LayoutMetrics(8, 18, 14),
            new MonospaceLineLayoutEngine());

        Assert.InRange(layouts.Count, 1, 10);
        Assert.All(layouts, layout => Assert.NotNull(layout.TextLayout));
    }

    [Fact]
    public void Inlay_layout_keeps_adornment_identity_and_visual_width()
    {
        var snapshot = new TextSnapshot("x");
        var projection = TextProjectionBuilder.Build(
            snapshot,
            inlays: new[]
            {
                new InlineAdornment(
                    "type-hint",
                    DocumentAnchor.After(1),
                    "type",
                    new AdornmentContent(": int"))
            });
        var layout = new MonospaceLineLayoutEngine().Layout(
            snapshot,
            projection.Lines[0],
            Array.Empty<SyntaxSpan>(),
            new LayoutMetrics(8, 18, 14));

        var inlayRun = Assert.Single(layout.Runs, run => run.Kind == LayoutRunKind.InlineAdornment);
        Assert.Equal("type", inlayRun.AdornmentKind);
        Assert.Equal(DocumentAnchor.After(1), inlayRun.Anchor);
        Assert.Equal(48, layout.Width);
    }

    [Fact]
    public void Viewport_layout_realizes_only_the_requested_window()
    {
        var snapshot = new TextSnapshot("a\nb\nc\nd\ne");
        var projection = TextProjectionBuilder.Build(snapshot);
        var heights = new VisualLineHeightIndex(Enumerable.Repeat(18d, projection.VisualLineCount));
        var layouts = ViewportLayoutEngine.LayoutVisible(
            snapshot,
            projection,
            heights,
            new LayoutViewport(18, 18),
            overscan: 0,
            Array.Empty<SyntaxSpan>(),
            new LayoutMetrics(8, 18, 14),
            new MonospaceLineLayoutEngine());

        Assert.Equal(2, layouts.Count);
        Assert.Equal(1, layouts[0].SourceLine.LogicalLine);
        Assert.Equal(2, layouts[1].SourceLine.LogicalLine);
    }

    [Fact]
    public void Viewport_layout_rejects_a_projection_from_another_snapshot()
    {
        var projection = TextProjectionBuilder.Build(new TextSnapshot("a"));
        var otherSnapshot = new TextSnapshot("a");
        var heights = new VisualLineHeightIndex(new[] { 18d });
        Assert.Throws<ArgumentException>(() => ViewportLayoutEngine.LayoutVisible(
            otherSnapshot,
            projection,
            heights,
            new LayoutViewport(0, 18),
            0,
            Array.Empty<SyntaxSpan>(),
            new LayoutMetrics(8, 18, 14),
            new MonospaceLineLayoutEngine()));
    }

    [Fact]
    public void Visual_rows_insert_block_adornments_before_and_after_text_rows()
    {
        var snapshot = new TextSnapshot("a\nb");
        var projection = TextProjectionBuilder.Build(snapshot);
        var rows = VisualRowMapBuilder.Build(
            projection,
            new[]
            {
                new BlockAdornment(
                    "before",
                    DocumentAnchor.Before(0),
                    12,
                    "codelens",
                    new AdornmentContent("before")),
                new BlockAdornment(
                    "after",
                    DocumentAnchor.After(0),
                    8,
                    "codelens",
                    new AdornmentContent("after"))
            });

        Assert.Equal(
            new[]
            {
                VisualRowKind.BlockAdornment,
                VisualRowKind.Text,
                VisualRowKind.BlockAdornment,
                VisualRowKind.Text
            },
            rows.Rows.Select(row => row.Kind));
        Assert.Equal(0, rows.Rows[0].LogicalLine);
        Assert.Equal(1, rows.Rows[3].LogicalLine);
    }

    [Fact]
    public void Visual_row_layout_uses_block_heights_and_keeps_text_layouts_snapshot_bound()
    {
        var snapshot = new TextSnapshot("a\nb");
        var projection = TextProjectionBuilder.Build(snapshot);
        var rows = VisualRowMapBuilder.Build(
            projection,
            new[]
            {
                new BlockAdornment(
                    "lens",
                    DocumentAnchor.Before(0),
                    12,
                    "codelens",
                    new AdornmentContent("details"))
            });
        var heights = new VisualLineHeightIndex(
            rows.Rows.Select(row => row.BlockAdornment?.DesiredHeight ?? 18));

        var layouts = ViewportLayoutEngine.LayoutVisibleRows(
            snapshot,
            rows,
            heights,
            new LayoutViewport(0, 29),
            overscan: 0,
            Array.Empty<SyntaxSpan>(),
            new LayoutMetrics(8, 18, 14),
            new MonospaceLineLayoutEngine());

        Assert.Equal(2, layouts.Count);
        Assert.Null(layouts[0].TextLayout);
        Assert.Equal(12, layouts[0].Height);
        Assert.Equal(0, layouts[1].TextLayout!.SourceLine.LogicalLine);
        Assert.Equal(12, layouts[1].Top);
    }

    [Fact]
    public void Blocks_inside_a_fold_are_not_realized()
    {
        var snapshot = new TextSnapshot("a\nb");
        var projection = TextProjectionBuilder.Build(
            snapshot,
            new[] { new FoldRange("fold", new TextRange(0, 2)) });
        var rows = VisualRowMapBuilder.Build(
            projection,
            new[]
            {
                new BlockAdornment(
                    "hidden",
                    DocumentAnchor.Before(1),
                    12,
                    "codelens",
                    new AdornmentContent("hidden"))
            });

        Assert.DoesNotContain(rows.Rows, row => row.BlockAdornment?.Id == "hidden");
    }

    [Fact]
    public void Viewport_layout_clamps_an_offset_left_over_after_projection_shrinks()
    {
        var snapshot = new TextSnapshot("a");
        var projection = TextProjectionBuilder.Build(snapshot);
        var rows = VisualRowMapBuilder.Build(projection);
        var heights = new VisualLineHeightIndex(new[] { 18d });

        var layouts = ViewportLayoutEngine.LayoutVisibleRows(
            snapshot,
            rows,
            heights,
            new LayoutViewport(100, 18),
            overscan: 0,
            Array.Empty<SyntaxSpan>(),
            new LayoutMetrics(8, 18, 14),
            new MonospaceLineLayoutEngine());

        Assert.Single(layouts);
        Assert.Equal(0, layouts[0].Top);
    }

    private static (VisualRowKind Kind, int LogicalLine, int Start, int Length, string? BlockId)
        DescribeRow(VisualRow row) =>
        (row.Kind, row.LogicalLine, row.TextStartColumn, row.TextLength, row.BlockAdornment?.Id);
}
