namespace Azunyan.Core;

public enum VisualRowKind
{
    Text,
    BlockAdornment
}

public readonly record struct VisualRowChangeWindow(
    int OldStart,
    int OldEnd,
    int NewStart,
    int NewEnd);

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
            && wrappedLineBreaksByVisualLine is not { Count: > 0 })
        {
            return VisualRowMap.CreatePlain(projection);
        }

        var normalizedBlocks = NormalizeBlocks(projection, blocks);
        var wrapBreaks = CreateWrapBreakMap(
            projection,
            wrappedLineBreaks,
            wrappedLineBreaksByVisualLine);
        var rows = BuildRows(
            projection,
            normalizedBlocks,
            wrapColumns,
            wrappedLineBreaks,
            wrappedLineBreaksByVisualLine,
            0,
            projection.Lines.Count,
            0);
        return new VisualRowMap(projection, rows, normalizedBlocks, wrapBreaks);
    }

    public static VisualRowMap BuildIncremental(
        TextProjection previousProjection,
        TextProjection projection,
        VisualRowMap previous,
        IEnumerable<BlockAdornment>? blockAdornments = null,
        int wrapColumns = 0,
        IReadOnlyDictionary<int, IReadOnlyList<int>>? wrappedLineBreaksByVisualLine = null,
        TextChange? change = null)
    {
        ArgumentNullException.ThrowIfNull(previousProjection);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentOutOfRangeException.ThrowIfNegative(wrapColumns);
        if (wrappedLineBreaksByVisualLine is not null
            && wrappedLineBreaksByVisualLine.Keys.Any(index =>
                index < 0 || index >= projection.Lines.Count))
        {
            throw new ArgumentException(
                "Measured wrap breaks contain an invalid projected line index.",
                nameof(wrappedLineBreaksByVisualLine));
        }

        if (!ReferenceEquals(previous.Projection, previousProjection)
            || projection.ChangeWindow is not { } changeWindow
            || !projection.IsPlain
                && previousProjection.IsPlain
                && !ReferenceEquals(previousProjection.Snapshot, projection.Snapshot))
        {
            return Build(projection, blockAdornments, wrapColumns, wrappedLineBreaksByVisualLine: wrappedLineBreaksByVisualLine);
        }

        var blockArray = blockAdornments?.ToArray() ?? Array.Empty<BlockAdornment>();
        if (blockArray.Length == 0
            && wrapColumns == 0
            && wrappedLineBreaksByVisualLine is not { Count: > 0 })
        {
            return VisualRowMap.CreatePlain(projection);
        }

        var wrapBreaks = CreateWrapBreakMap(
            projection,
            wrappedLineBreaks: null,
            wrappedLineBreaksByVisualLine);
        CarryForwardWrapBreaks(
            previousProjection,
            projection,
            previous.WrapBreaks,
            wrapBreaks,
            changeWindow);
        var blocks = NormalizeBlocks(
            projection,
            blockArray);
        var oldVisualWindow = GetVisualWindow(
            previousProjection,
            changeWindow.OldStartLine,
            changeWindow.OldEndLine);
        var newVisualWindow = GetVisualWindow(
            projection,
            changeWindow.NewStartLine,
            changeWindow.NewEndLine);
        if (oldVisualWindow.Start != newVisualWindow.Start)
        {
            return Build(
                projection,
                blocks,
                wrapColumns,
                wrappedLineBreaksByVisualLine: wrappedLineBreaksByVisualLine);
        }

        ExpandForBlockChanges(
            previousProjection,
            projection,
            previous.BlockAdornments,
            blocks,
            change,
            ref oldVisualWindow,
            ref newVisualWindow);
        ExpandForWrapBreakChanges(
            previousProjection,
            projection,
            previous.WrapBreaks,
            wrapBreaks,
            changeWindow,
            ref oldVisualWindow,
            ref newVisualWindow);
        if (oldVisualWindow.Start != newVisualWindow.Start)
        {
            return Build(
                projection,
                blocks,
                wrapColumns,
                wrappedLineBreaksByVisualLine: wrappedLineBreaksByVisualLine);
        }

        var oldRowWindow = previous.GetRowRange(oldVisualWindow.Start, oldVisualWindow.End);
        var newRowStart = oldRowWindow.Start;
        var newRowCounts = GetRowCounts(
            projection,
            blocks,
            wrapColumns,
            newVisualWindow.Start,
            newVisualWindow.End,
            wrapBreaks);
        var newRowEnd = checked(newRowStart + newRowCounts.Sum());
        var changedRows = BuildRows(
            projection,
            blocks,
            wrapColumns,
            wrappedLineBreaks: null,
            wrappedLineBreaksByVisualLine: wrappedLineBreaksByVisualLine,
            newVisualWindow.Start,
            newVisualWindow.End,
            newRowStart);
        if (newRowStart + changedRows.Count != newRowEnd)
        {
            return Build(
                projection,
                blocks,
                wrapColumns,
                wrappedLineBreaksByVisualLine: wrappedLineBreaksByVisualLine);
        }
        var blocksById = blocks.ToDictionary(block => block.Id, StringComparer.Ordinal);
        var rows = new IncrementalVisualRowList(
            previous.Rows,
            changedRows,
            projection,
            blocksById,
            oldRowWindow.Start,
            oldRowWindow.End,
            changeWindow.NewEndLine - changeWindow.OldEndLine);
        return new VisualRowMap(
            projection,
            rows,
            blocks,
            wrapBreaks,
            previous.RowCounts.Replace(
                oldVisualWindow.Start,
                oldVisualWindow.End - oldVisualWindow.Start,
                newRowCounts),
            new VisualRowChangeWindow(
                oldRowWindow.Start,
                oldRowWindow.End,
                newRowStart,
                newRowEnd));
    }

    private static (int Start, int End) GetVisualWindow(
        TextProjection projection,
        int startLogicalLine,
        int endLogicalLine)
    {
        var start = Math.Clamp(startLogicalLine, 0, projection.Snapshot.Lines.LineCount);
        var end = Math.Clamp(endLogicalLine, start, projection.Snapshot.Lines.LineCount);
        var first = int.MaxValue;
        var last = -1;
        for (var logicalLine = start; logicalLine < end; logicalLine++)
        {
            if (projection.TryGetVisualLine(logicalLine, out var visualLine))
            {
                first = Math.Min(first, visualLine);
                last = Math.Max(last, visualLine);
            }
        }

        if (last >= 0)
        {
            return (first, last + 1);
        }

        for (var logicalLine = end; logicalLine < projection.Snapshot.Lines.LineCount; logicalLine++)
        {
            if (projection.TryGetVisualLine(logicalLine, out var visualLine))
            {
                return (visualLine, visualLine);
            }
        }

        return (projection.Lines.Count, projection.Lines.Count);
    }

    private static int[] GetRowCounts(
        TextProjection projection,
        IReadOnlyList<BlockAdornment> blocks,
        int wrapColumns,
        int startVisualLine,
        int endVisualLine,
        IReadOnlyDictionary<WrapBreakKey, IReadOnlyList<int>> wrapBreaks)
    {
        var blocksByVisualLine = blocks
            .GroupBy(block => projection.MapDocumentPosition(block.Anchor).VisualLine)
            .ToDictionary(group => group.Key, group => group.Count());
        var counts = new int[endVisualLine - startVisualLine];
        for (var visualLine = startVisualLine; visualLine < endVisualLine; visualLine++)
        {
            var count = blocksByVisualLine.TryGetValue(visualLine, out var blockCount)
                ? blockCount
                : 0;
            var line = projection.Lines[visualLine];
            counts[visualLine - startVisualLine] = checked(
                count + GetTextRowCount(line, wrapColumns, wrapBreaks));
        }

        return counts;
    }

    private static int GetTextRowCount(
        ProjectedLine line,
        int wrapColumns,
        IReadOnlyDictionary<WrapBreakKey, IReadOnlyList<int>> wrapBreaks)
    {
        if (line.VisualLength == 0 || wrapColumns <= 0)
        {
            return 1;
        }

        return wrapBreaks.TryGetValue(CreateWrapBreakKey(line), out var measuredBreaks)
            ? checked(measuredBreaks.Count + 1)
            : (line.VisualLength + wrapColumns - 1) / wrapColumns;
    }

    private static Dictionary<WrapBreakKey, IReadOnlyList<int>> CreateWrapBreakMap(
        TextProjection projection,
        IReadOnlyList<IReadOnlyList<int>?>? wrappedLineBreaks,
        IReadOnlyDictionary<int, IReadOnlyList<int>>? wrappedLineBreaksByVisualLine)
    {
        var result = new Dictionary<WrapBreakKey, IReadOnlyList<int>>();
        if (wrappedLineBreaks is not null)
        {
            for (var visualLine = 0; visualLine < wrappedLineBreaks.Count; visualLine++)
            {
                if (wrappedLineBreaks[visualLine] is { } breaks)
                {
                    result[CreateWrapBreakKey(projection.Lines[visualLine])] = breaks;
                }
            }

            return result;
        }

        if (wrappedLineBreaksByVisualLine is not null)
        {
            foreach (var pair in wrappedLineBreaksByVisualLine)
            {
                result[CreateWrapBreakKey(projection.Lines[pair.Key])] = pair.Value;
            }
        }

        return result;
    }

    internal static WrapBreakKey CreateWrapBreakKey(ProjectedLine line) =>
        new(line.LogicalLine, line.SourceRange.Start, line.SourceRange.End);

    private static void ExpandForWrapBreakChanges(
        TextProjection previousProjection,
        TextProjection projection,
        IReadOnlyDictionary<WrapBreakKey, IReadOnlyList<int>> previous,
        Dictionary<WrapBreakKey, IReadOnlyList<int>> current,
        ProjectionChangeWindow changeWindow,
        ref (int Start, int End) oldWindow,
        ref (int Start, int End) newWindow)
    {
        var logicalDelta = changeWindow.NewEndLine - changeWindow.OldEndLine;
        foreach (var pair in previous)
        {
            if (!previousProjection.TryGetVisualLine(pair.Key.LogicalLine, out var oldVisualLine)
                || IsInside(oldVisualLine, oldWindow))
            {
                continue;
            }

            var newLogicalLine = pair.Key.LogicalLine >= changeWindow.OldEndLine
                ? checked(pair.Key.LogicalLine + logicalDelta)
                : pair.Key.LogicalLine;
            if (!projection.TryGetVisualLine(newLogicalLine, out var newVisualLine))
            {
                ExpandWindow(ref oldWindow, oldVisualLine);
                continue;
            }

            var newKey = CreateWrapBreakKey(projection.Lines[newVisualLine]);
            if (!current.TryGetValue(newKey, out var currentBreaks)
                || !BreaksEqual(pair.Value, currentBreaks))
            {
                ExpandWindow(ref oldWindow, oldVisualLine);
                ExpandWindow(ref newWindow, newVisualLine);
            }
        }

        foreach (var pair in current)
        {
            if (!projection.TryGetVisualLine(pair.Key.LogicalLine, out var newVisualLine)
                || IsInside(newVisualLine, newWindow))
            {
                continue;
            }

            var oldLogicalLine = pair.Key.LogicalLine >= changeWindow.NewEndLine
                ? checked(pair.Key.LogicalLine - logicalDelta)
                : pair.Key.LogicalLine;
            if (!previousProjection.TryGetVisualLine(oldLogicalLine, out var oldVisualLine))
            {
                ExpandWindow(ref newWindow, newVisualLine);
                continue;
            }

            var oldKey = CreateWrapBreakKey(previousProjection.Lines[oldVisualLine]);
            if (!previous.TryGetValue(oldKey, out var previousBreaks)
                || !BreaksEqual(previousBreaks, pair.Value))
            {
                ExpandWindow(ref oldWindow, oldVisualLine);
                ExpandWindow(ref newWindow, newVisualLine);
            }
        }
    }

    private static void CarryForwardWrapBreaks(
        TextProjection previousProjection,
        TextProjection projection,
        IReadOnlyDictionary<WrapBreakKey, IReadOnlyList<int>> previous,
        Dictionary<WrapBreakKey, IReadOnlyList<int>> current,
        ProjectionChangeWindow changeWindow)
    {
        var logicalDelta = changeWindow.NewEndLine - changeWindow.OldEndLine;
        foreach (var pair in previous)
        {
            if (pair.Key.LogicalLine >= changeWindow.OldStartLine
                && pair.Key.LogicalLine < changeWindow.OldEndLine)
            {
                continue;
            }

            var newLogicalLine = pair.Key.LogicalLine >= changeWindow.OldEndLine
                ? checked(pair.Key.LogicalLine + logicalDelta)
                : pair.Key.LogicalLine;
            if (!projection.TryGetVisualLine(newLogicalLine, out var newVisualLine))
            {
                continue;
            }

            var newKey = CreateWrapBreakKey(projection.Lines[newVisualLine]);
            current.TryAdd(newKey, pair.Value);
        }
    }

    private static bool IsInside(int value, (int Start, int End) window) =>
        value >= window.Start && value < window.End;

    private static void ExpandWindow(ref (int Start, int End) window, int visualLine)
    {
        window.Start = Math.Min(window.Start, visualLine);
        window.End = Math.Max(window.End, visualLine + 1);
    }

    private static bool BreaksEqual(
        IReadOnlyList<int> previous,
        IReadOnlyList<int> current) =>
        previous.SequenceEqual(current);

    private static void ExpandForBlockChanges(
        TextProjection previousProjection,
        TextProjection projection,
        IReadOnlyList<BlockAdornment> previous,
        IReadOnlyList<BlockAdornment> current,
        TextChange? change,
        ref (int Start, int End) oldWindow,
        ref (int Start, int End) newWindow)
    {
        var currentById = current.ToDictionary(block => block.Id, StringComparer.Ordinal);
        var matched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var oldBlock in previous)
        {
            if (currentById.TryGetValue(oldBlock.Id, out var currentBlock)
                && BlockMatches(oldBlock, currentBlock, change))
            {
                matched.Add(oldBlock.Id);
                continue;
            }

            ExpandForBlock(previousProjection, oldBlock, ref oldWindow);
            if (currentBlock is not null)
            {
                ExpandForBlock(projection, currentBlock, ref newWindow);
                matched.Add(currentBlock.Id);
            }
        }

        foreach (var currentBlock in current)
        {
            if (!matched.Contains(currentBlock.Id))
            {
                ExpandForBlock(projection, currentBlock, ref newWindow);
            }
        }
    }

    private static bool BlockMatches(
        BlockAdornment previous,
        BlockAdornment current,
        TextChange? change)
    {
        var delta = change is { } documentChange
            ? documentChange.NewText.Length - documentChange.OldRange.Length
            : 0;
        var mappedPosition = change is { } mappedChange
            ? MapPosition(previous.Anchor.Position.Offset, mappedChange, delta)
            : previous.Anchor.Position.Offset;
        return current.Anchor.Position.Offset == mappedPosition
            && current.Anchor.Affinity == previous.Anchor.Affinity
            && current.DesiredHeight == previous.DesiredHeight
            && current.Kind == previous.Kind
            && ContentMatches(previous.Content, current.Content);
    }

    private static void ExpandForBlock(
        TextProjection projection,
        BlockAdornment block,
        ref (int Start, int End) window)
    {
        var visualLine = projection.MapDocumentPosition(block.Anchor).VisualLine;
        window.Start = Math.Min(window.Start, visualLine);
        window.End = Math.Max(window.End, visualLine + 1);
    }

    private static int MapPosition(int position, TextChange change, int delta) =>
        position <= change.OldRange.Start
            ? position
            : position >= change.OldRange.End
                ? checked(position + delta)
                : change.NewRange.Start;

    private static bool ContentMatches(AdornmentContent previous, AdornmentContent current)
    {
        if (previous.Text != current.Text
            || previous.IconKey != current.IconKey
            || previous.Actions.Count != current.Actions.Count)
        {
            return false;
        }

        return previous.Actions.Zip(current.Actions).All(pair =>
            pair.First.Id == pair.Second.Id
            && pair.First.Label == pair.Second.Label
            && pair.First.CommandId == pair.Second.CommandId);
    }

    private static List<VisualRow> BuildRows(
        TextProjection projection,
        IReadOnlyList<BlockAdornment> blocks,
        int wrapColumns,
        IReadOnlyList<IReadOnlyList<int>?>? wrappedLineBreaks,
        IReadOnlyDictionary<int, IReadOnlyList<int>>? wrappedLineBreaksByVisualLine,
        int startVisualLine,
        int endVisualLine,
        int rowIndexOffset)
    {
        var blocksByVisualLine = blocks
            .GroupBy(block => projection.MapDocumentPosition(block.Anchor).VisualLine)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var rows = new List<VisualRow>();
        for (var visualLine = startVisualLine; visualLine < endVisualLine; visualLine++)
        {
            var line = projection.Lines[visualLine];
            if (blocksByVisualLine.TryGetValue(visualLine, out var lineBlocks))
            {
                foreach (var block in lineBlocks.Where(IsBefore))
                {
                    rows.Add(new VisualRow(rowIndexOffset + rows.Count, line.LogicalLine, null, block));
                }
            }

            var measuredBreaks = wrappedLineBreaks is not null
                ? wrappedLineBreaks[visualLine]
                : wrappedLineBreaksByVisualLine is not null
                    && wrappedLineBreaksByVisualLine.TryGetValue(visualLine, out var measured)
                    ? measured
                    : null;
            AddTextRows(rows, line, wrapColumns, measuredBreaks, rowIndexOffset);

            if (lineBlocks is not null)
            {
                foreach (var block in lineBlocks.Where(block => !IsBefore(block)))
                {
                    rows.Add(new VisualRow(rowIndexOffset + rows.Count, line.LogicalLine, null, block));
                }
            }
        }

        return rows;
    }

    private static void AddTextRows(
        List<VisualRow> rows,
        ProjectedLine line,
        int wrapColumns,
        IReadOnlyList<int>? measuredBreaks,
        int rowIndexOffset)
    {
        if (line.VisualLength == 0)
        {
            rows.Add(new VisualRow(rowIndexOffset + rows.Count, line.LogicalLine, line, null));
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
                    rowIndexOffset + rows.Count,
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
            rows.Add(new VisualRow(rowIndexOffset + rows.Count, line.LogicalLine, line, null));
            return;
        }

        for (var start = 0; start < line.VisualLength; start += wrapColumns)
        {
            rows.Add(new VisualRow(
                rowIndexOffset + rows.Count,
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
    private readonly bool _isPlain;
    internal VisualRowCountIndex RowCounts { get; }

    internal VisualRowMap(TextProjection projection, IReadOnlyList<VisualRow> rows)
        : this(
            projection,
            rows,
            Array.Empty<BlockAdornment>(),
            new Dictionary<WrapBreakKey, IReadOnlyList<int>>(),
            rowCounts: null,
            changeWindow: null,
            isPlain: false)
    {
    }

    internal VisualRowMap(
        TextProjection projection,
        IReadOnlyList<VisualRow> rows,
        IReadOnlyList<BlockAdornment> blockAdornments)
        : this(
            projection,
            rows,
            blockAdornments,
            new Dictionary<WrapBreakKey, IReadOnlyList<int>>(),
            rowCounts: null,
            changeWindow: null,
            isPlain: false)
    {
    }

    internal VisualRowMap(
        TextProjection projection,
        IReadOnlyList<VisualRow> rows,
        IReadOnlyList<BlockAdornment> blockAdornments,
        IReadOnlyDictionary<WrapBreakKey, IReadOnlyList<int>> wrapBreaks)
        : this(projection, rows, blockAdornments, wrapBreaks, rowCounts: null, changeWindow: null, isPlain: false)
    {
    }

    internal VisualRowMap(
        TextProjection projection,
        IReadOnlyList<VisualRow> rows,
        IReadOnlyList<BlockAdornment> blockAdornments,
        IReadOnlyDictionary<WrapBreakKey, IReadOnlyList<int>> wrapBreaks,
        VisualRowCountIndex rowCounts,
        VisualRowChangeWindow changeWindow)
        : this(projection, rows, blockAdornments, wrapBreaks, rowCounts, changeWindow, isPlain: false)
    {
    }

    private VisualRowMap(
        TextProjection projection,
        IReadOnlyList<VisualRow> rows,
        IReadOnlyList<BlockAdornment> blockAdornments,
        IReadOnlyDictionary<WrapBreakKey, IReadOnlyList<int>> wrapBreaks,
        VisualRowCountIndex? rowCounts,
        VisualRowChangeWindow? changeWindow,
        bool isPlain)
    {
        Projection = projection;
        Rows = rows;
        BlockAdornments = blockAdornments.ToArray();
        WrapBreaks = wrapBreaks;
        ChangeWindow = changeWindow;
        _isPlain = isPlain;
        RowCounts = rowCounts ?? CreateRowCounts(projection, rows);
    }

    public TextProjection Projection { get; }

    public IReadOnlyList<VisualRow> Rows { get; }

    internal IReadOnlyList<BlockAdornment> BlockAdornments { get; }

    internal IReadOnlyDictionary<WrapBreakKey, IReadOnlyList<int>> WrapBreaks { get; }

    public VisualRowChangeWindow? ChangeWindow { get; }

    public IReadOnlyDictionary<int, IReadOnlyList<int>> GetWrapBreaksByVisualLine()
    {
        var result = new Dictionary<int, IReadOnlyList<int>>();
        foreach (var pair in WrapBreaks)
        {
            if (!Projection.TryGetVisualLine(pair.Key.LogicalLine, out var visualLine))
            {
                continue;
            }

            var line = Projection.Lines[visualLine];
            if (VisualRowMapBuilder.CreateWrapBreakKey(line) == pair.Key)
            {
                result[visualLine] = pair.Value;
            }
        }

        return result;
    }

    public bool HasUniformTextHeights => BlockAdornments.Count == 0;

    internal static VisualRowMap CreatePlain(TextProjection projection) =>
        new(
            projection,
            new PlainVisualRowList(projection),
            Array.Empty<BlockAdornment>(),
            new Dictionary<WrapBreakKey, IReadOnlyList<int>>(),
            VisualRowCountIndex.CreateUniform(projection.Lines.Count, 1),
            changeWindow: null,
            isPlain: true);

    public IReadOnlyList<int> GetTextRowIndices(ProjectedLine line)
    {
        if (!ProjectionLineMatches(line, out var visualLine)) return Array.Empty<int>();
        var range = RowCounts.GetRowRange(visualLine, visualLine + 1);
        return Enumerable.Range(range.Start, range.End - range.Start)
            .Where(index => ReferenceEquals(
                Rows[index].TextLine?.LayoutCacheIdentity,
                line.LayoutCacheIdentity))
            .ToArray();
    }

    public IReadOnlyList<int> GetBlockRowIndices(DocumentAnchor anchor)
    {
        var visualLine = Projection.MapDocumentPosition(anchor).VisualLine;
        var range = RowCounts.GetRowRange(visualLine, visualLine + 1);
        return Enumerable.Range(range.Start, range.End - range.Start)
            .Where(index => Rows[index].BlockAdornment?.Anchor == anchor)
            .ToArray();
    }

    internal (int Start, int End) GetRowRange(int startVisualLine, int endVisualLine) =>
        RowCounts.GetRowRange(startVisualLine, endVisualLine);

    private bool ProjectionLineMatches(ProjectedLine line, out int visualRow)
    {
        return Projection.TryGetVisualLine(line.LogicalLine, out visualRow)
            && ReferenceEquals(Projection.Lines[visualRow], line);
    }

    private static VisualRowCountIndex CreateRowCounts(
        TextProjection projection,
        IReadOnlyList<VisualRow> rows)
    {
        var counts = new int[projection.Lines.Count];
        foreach (var row in rows)
        {
            var logicalLine = row.TextLine?.LogicalLine ?? row.LogicalLine;
            if (!projection.TryGetVisualLine(logicalLine, out var visualLine))
            {
                throw new InvalidOperationException("A visual row does not belong to its projection.");
            }

            counts[visualLine]++;
        }

        return VisualRowCountIndex.Create(counts);
    }
}
