using Azunyan.Core;

namespace Azunyan.Layout;

public readonly record struct LayoutViewport
{
    public LayoutViewport(double verticalOffset, double height)
    {
        if (!double.IsFinite(verticalOffset) || verticalOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(verticalOffset));
        }

        if (!double.IsFinite(height) || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        VerticalOffset = verticalOffset;
        Height = height;
    }

    public double VerticalOffset { get; }

    public double Height { get; }

    public double GetOffsetToReveal(double startOffset, double endOffset)
    {
        if (!double.IsFinite(startOffset)
            || !double.IsFinite(endOffset)
            || startOffset < 0
            || endOffset < startOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(startOffset));
        }

        if (startOffset < VerticalOffset)
        {
            return startOffset;
        }

        return endOffset > VerticalOffset + Height
            ? endOffset - Height
            : VerticalOffset;
    }
}

public sealed class ViewportRowLayout
{
    internal ViewportRowLayout(
        VisualRow row,
        double top,
        double height,
        UnwrappedLineLayout? textLayout)
    {
        Row = row;
        Top = top;
        Height = height;
        TextLayout = textLayout;
    }

    public VisualRow Row { get; }

    public double Top { get; }

    public double Height { get; }

    public UnwrappedLineLayout? TextLayout { get; }
}

/// <summary>
/// Selects a bounded visual-line window. It does not create UI elements and
/// can therefore be used by a renderer or a background pre-layout pass.
/// </summary>
public sealed class ViewportLayoutEngine
{
    public static IReadOnlyList<ViewportRowLayout> LayoutVisibleRows(
        TextSnapshot snapshot,
        VisualRowMap rowMap,
        VisualLineHeightIndex heights,
        LayoutViewport viewport,
        double overscan,
        IReadOnlyList<SyntaxSpan> syntax,
        LayoutMetrics metrics,
        IUnwrappedLineLayoutEngine lineEngine,
        IDictionary<ProjectedLine, UnwrappedLineLayout>? lineLayoutCache = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(rowMap);
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentNullException.ThrowIfNull(syntax);
        ArgumentNullException.ThrowIfNull(lineEngine);
        if (!double.IsFinite(overscan) || overscan < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(overscan));
        }

        if (!ReferenceEquals(rowMap.Projection.Snapshot, snapshot)
            || heights.Count != rowMap.Rows.Count)
        {
            throw new ArgumentException("The snapshot, row map, and height index must describe the same frame.");
        }

        if (rowMap.Rows.Count == 0)
        {
            return Array.Empty<ViewportRowLayout>();
        }

        var startOffset = Math.Min(
            heights.TotalHeight,
            Math.Max(0, viewport.VerticalOffset - overscan));
        var endOffset = Math.Min(
            heights.TotalHeight,
            viewport.VerticalOffset + viewport.Height + overscan);
        var first = heights.FindLine(startOffset);
        var last = heights.FindLine(endOffset);
        var result = new List<ViewportRowLayout>(last - first + 1);
        for (var visualRow = first; visualRow <= last; visualRow++)
        {
            var row = rowMap.Rows[visualRow];
            var textLayout = row.TextLine is null
                ? null
                : LayoutTextRow(
                    snapshot,
                    row,
                    syntax,
                    metrics,
                    lineEngine,
                    lineLayoutCache);
            result.Add(new ViewportRowLayout(
                row,
                heights.GetOffset(visualRow),
                heights.GetHeight(visualRow),
                textLayout));
        }

        return result;
    }

    private static UnwrappedLineLayout LayoutTextRow(
        TextSnapshot snapshot,
        VisualRow row,
        IReadOnlyList<SyntaxSpan> syntax,
        LayoutMetrics metrics,
        IUnwrappedLineLayoutEngine lineEngine,
        IDictionary<ProjectedLine, UnwrappedLineLayout>? lineLayoutCache)
    {
        var textLine = row.TextLine!;
        if (lineLayoutCache is null
            || !lineLayoutCache.TryGetValue(textLine, out var full))
        {
            full = lineEngine.Layout(snapshot, textLine, syntax, metrics);
            lineLayoutCache?.Add(textLine, full);
        }

        return row.TextStartColumn == 0
            && row.TextLength == textLine.VisualLength
            ? full
            : full.Slice(row.TextStartColumn, row.TextLength);
    }

    public static IReadOnlyList<UnwrappedLineLayout> LayoutVisible(
        TextSnapshot snapshot,
        TextProjection projection,
        VisualLineHeightIndex heights,
        LayoutViewport viewport,
        double overscan,
        IReadOnlyList<SyntaxSpan> syntax,
        LayoutMetrics metrics,
        IUnwrappedLineLayoutEngine lineEngine)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentNullException.ThrowIfNull(syntax);
        ArgumentNullException.ThrowIfNull(lineEngine);
        if (!double.IsFinite(overscan) || overscan < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(overscan));
        }

        if (!ReferenceEquals(projection.Snapshot, snapshot) || heights.Count != projection.VisualLineCount)
        {
            throw new ArgumentException("The snapshot, projection, and height index must describe the same frame.");
        }

        if (projection.VisualLineCount == 0)
        {
            return Array.Empty<UnwrappedLineLayout>();
        }

        var startOffset = Math.Min(
            heights.TotalHeight,
            Math.Max(0, viewport.VerticalOffset - overscan));
        var endOffset = Math.Min(heights.TotalHeight, viewport.VerticalOffset + viewport.Height + overscan);
        var first = heights.FindLine(startOffset);
        var last = heights.FindLine(endOffset);
        var result = new List<UnwrappedLineLayout>(last - first + 1);
        for (var visualLine = first; visualLine <= last; visualLine++)
        {
            result.Add(lineEngine.Layout(snapshot, projection.Lines[visualLine], syntax, metrics));
        }

        return result;
    }
}
