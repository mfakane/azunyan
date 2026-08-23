namespace Azunyan.Core;

public enum VisualRowKind
{
    Text,
    BlockAdornment
}

/// <summary>
/// One row in the projected document. Text rows reference a projected logical
/// line; block rows occupy vertical space without changing document text.
/// </summary>
public sealed class VisualRow
{
    internal VisualRow(
        int visualRow,
        int logicalLine,
        ProjectedLine? textLine,
        BlockAdornment? blockAdornment,
        int textStartColumn = 0,
        int? textLength = null)
    {
        VisualRowIndex = visualRow;
        LogicalLine = logicalLine;
        TextLine = textLine;
        BlockAdornment = blockAdornment;
        Kind = textLine is null ? VisualRowKind.BlockAdornment : VisualRowKind.Text;
        if (textLine is not null)
        {
            if (textStartColumn < 0 || textStartColumn > textLine.VisualLength)
            {
                throw new ArgumentOutOfRangeException(nameof(textStartColumn));
            }

            var length = textLength ?? (textLine.VisualLength - textStartColumn);
            if (length < 0 || textStartColumn + length > textLine.VisualLength)
            {
                throw new ArgumentOutOfRangeException(nameof(textLength));
            }

            TextStartColumn = textStartColumn;
            TextLength = length;
        }
    }

    public int VisualRowIndex { get; }

    public int LogicalLine { get; }

    public VisualRowKind Kind { get; }

    public ProjectedLine? TextLine { get; }

    public BlockAdornment? BlockAdornment { get; }

    /// <summary>
    /// The global projected-column interval represented by this text row.
    /// Unwrapped rows start at zero; continuation rows start after the wrap
    /// boundary.
    /// </summary>
    public int TextStartColumn { get; }

    public int TextLength { get; }

    public int TextEndColumn => checked(TextStartColumn + TextLength);

    public bool IsContinuation => Kind == VisualRowKind.Text && TextStartColumn > 0;
}

/// <summary>
/// Builds the ordered visual rows for one immutable projection. Block
/// adornments are inserted before or after their anchored logical line using
/// anchor affinity, and hidden blocks inside a fold are omitted.
/// </summary>
public sealed class VisualRowMapBuilder
{
    public VisualRowMap Build(
        TextProjection projection,
        IEnumerable<BlockAdornment>? blockAdornments = null,
        int wrapColumns = 0,
        IReadOnlyList<IReadOnlyList<int>?>? wrappedLineBreaks = null,
        IReadOnlyDictionary<int, IReadOnlyList<int>>? wrappedLineBreaksByVisualLine = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (wrapColumns < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(wrapColumns));
        }

        if (wrappedLineBreaks is not null
            && wrappedLineBreaks.Count != projection.Lines.Count)
        {
            throw new ArgumentException(
                "Measured wrap breaks must contain one entry per projected line.",
                nameof(wrappedLineBreaks));
        }

        if (wrappedLineBreaksByVisualLine is not null
            && wrappedLineBreaksByVisualLine.Keys.Any(index =>
                index < 0 || index >= projection.Lines.Count))
        {
            throw new ArgumentException(
                "Measured wrap breaks contain an invalid projected line index.",
                nameof(wrappedLineBreaksByVisualLine));
        }

        if (wrappedLineBreaks is not null
            && wrappedLineBreaksByVisualLine is not null)
        {
            throw new ArgumentException(
                "Specify either the full wrap-break list or the measured-line dictionary, not both.",
                nameof(wrappedLineBreaksByVisualLine));
        }

        var blocks = NormalizeBlocks(projection, blockAdornments ?? Array.Empty<BlockAdornment>());
        var blocksByVisualLine = blocks
            .GroupBy(block => projection.MapDocumentPosition(block.Anchor).VisualLine)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var rows = new List<VisualRow>();

        for (var visualLine = 0; visualLine < projection.Lines.Count; visualLine++)
        {
            var line = projection.Lines[visualLine];
            if (blocksByVisualLine.TryGetValue(visualLine, out var lineBlocks))
            {
                foreach (var block in lineBlocks.Where(IsBefore))
                {
                    rows.Add(new VisualRow(rows.Count, line.LogicalLine, null, block));
                }
            }

            var measuredBreaks = wrappedLineBreaks is not null
                ? wrappedLineBreaks[visualLine]
                : wrappedLineBreaksByVisualLine is not null
                    && wrappedLineBreaksByVisualLine.TryGetValue(visualLine, out var measured)
                    ? measured
                    : null;
            AddTextRows(rows, line, wrapColumns, measuredBreaks);

            if (lineBlocks is not null)
            {
                foreach (var block in lineBlocks.Where(block => !IsBefore(block)))
                {
                    rows.Add(new VisualRow(rows.Count, line.LogicalLine, null, block));
                }
            }
        }

        return new VisualRowMap(projection, rows);
    }

    private static void AddTextRows(
        ICollection<VisualRow> rows,
        ProjectedLine line,
        int wrapColumns,
        IReadOnlyList<int>? measuredBreaks)
    {
        if (line.VisualLength == 0)
        {
            rows.Add(new VisualRow(rows.Count, line.LogicalLine, line, null));
            return;
        }

        if (measuredBreaks is not null)
        {
            var start = 0;
            foreach (var end in measuredBreaks.Append(line.VisualLength))
            {
                if (end <= start || end > line.VisualLength)
                {
                    throw new ArgumentException(
                        "Measured wrap breaks must be strictly increasing and inside the projected line.",
                        nameof(measuredBreaks));
                }

                rows.Add(new VisualRow(
                    rows.Count,
                    line.LogicalLine,
                    line,
                    null,
                    start,
                    end - start));
                start = end;
            }

            return;
        }

        if (wrapColumns <= 0)
        {
            rows.Add(new VisualRow(rows.Count, line.LogicalLine, line, null));
            return;
        }

        for (var start = 0; start < line.VisualLength; start += wrapColumns)
        {
            rows.Add(new VisualRow(
                rows.Count,
                line.LogicalLine,
                line,
                null,
                start,
                Math.Min(wrapColumns, line.VisualLength - start)));
        }
    }

    private static IReadOnlyList<BlockAdornment> NormalizeBlocks(
        TextProjection projection,
        IEnumerable<BlockAdornment> candidates)
    {
        var accepted = new List<BlockAdornment>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var block in candidates
            .Where(block => block.Anchor.Position.Offset <= projection.Snapshot.Length)
            .Where(block => !projection.IsHidden(block.Anchor))
            .OrderBy(block => projection.MapDocumentPosition(block.Anchor).VisualLine)
            .ThenBy(block => IsBefore(block) ? 0 : 1)
            .ThenBy(block => block.Id, StringComparer.Ordinal))
        {
            if (ids.Add(block.Id))
            {
                accepted.Add(block);
            }
        }

        return accepted;
    }

    private static bool IsBefore(BlockAdornment block) =>
        block.Anchor.Affinity == AnchorAffinity.Before;
}

public sealed class VisualRowMap
{
    internal VisualRowMap(TextProjection projection, IReadOnlyList<VisualRow> rows)
    {
        Projection = projection;
        Rows = rows;
    }

    public TextProjection Projection { get; }

    public IReadOnlyList<VisualRow> Rows { get; }
}
