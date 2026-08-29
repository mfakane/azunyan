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
    public static VisualRowMap Build(
        TextProjection projection,
        IEnumerable<BlockAdornment>? blockAdornments = null,
        int wrapColumns = 0,
        IReadOnlyList<IReadOnlyList<int>?>? wrappedLineBreaks = null,
        IReadOnlyDictionary<int, IReadOnlyList<int>>? wrappedLineBreaksByVisualLine = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentOutOfRangeException.ThrowIfNegative(wrapColumns);

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

        var blocks = blockAdornments?.ToArray() ?? Array.Empty<BlockAdornment>();
        if (blocks.Length == 0
            && wrapColumns == 0
            && wrappedLineBreaks is null
            && wrappedLineBreaksByVisualLine is null)
        {
            return VisualRowMap.CreatePlain(projection);
        }

        var normalizedBlocks = NormalizeBlocks(projection, blocks);
        var blocksByVisualLine = normalizedBlocks
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
        List<VisualRow> rows,
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

    private static List<BlockAdornment> NormalizeBlocks(
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
    private readonly Dictionary<ProjectedLine, int[]> _textRowsByLine;
    private readonly Dictionary<DocumentAnchor, int[]> _blockRowsByAnchor;
    private readonly bool _isPlain;

    internal VisualRowMap(TextProjection projection, IReadOnlyList<VisualRow> rows)
        : this(projection, rows, isPlain: false)
    {
    }

    private VisualRowMap(
        TextProjection projection,
        IReadOnlyList<VisualRow> rows,
        bool isPlain)
    {
        Projection = projection;
        Rows = rows;
        _isPlain = isPlain;
        _textRowsByLine = isPlain
            ? new Dictionary<ProjectedLine, int[]>()
            : rows
                .Where(row => row.TextLine is not null)
                .GroupBy(row => row.TextLine!)
                .ToDictionary(group => group.Key, group => group
                    .Select(row => row.VisualRowIndex)
                    .ToArray());
        _blockRowsByAnchor = isPlain
            ? new Dictionary<DocumentAnchor, int[]>()
            : rows
                .Where(row => row.BlockAdornment is not null)
                .GroupBy(row => row.BlockAdornment!.Anchor)
                .ToDictionary(group => group.Key, group => group
                    .Select(row => row.VisualRowIndex)
                    .ToArray());
    }

    public TextProjection Projection { get; }

    public IReadOnlyList<VisualRow> Rows { get; }

    public bool HasUniformTextHeights => _isPlain;

    internal static VisualRowMap CreatePlain(TextProjection projection) =>
        new(projection, new PlainVisualRowList(projection), isPlain: true);

    public IReadOnlyList<int> GetTextRowIndices(ProjectedLine line) =>
        _isPlain && ProjectionLineMatches(line, out var plainRow)
            ? new[] { plainRow }
            : _textRowsByLine.TryGetValue(line, out var rows)
            ? rows
            : Array.Empty<int>();

    public IReadOnlyList<int> GetBlockRowIndices(DocumentAnchor anchor) =>
        _blockRowsByAnchor.TryGetValue(anchor, out var rows)
            ? rows
            : Array.Empty<int>();

    private bool ProjectionLineMatches(ProjectedLine line, out int visualRow)
    {
        visualRow = line.LogicalLine;
        return visualRow >= 0
            && visualRow < Projection.Lines.Count
            && ReferenceEquals(Projection.Lines[visualRow], line);
    }
}
