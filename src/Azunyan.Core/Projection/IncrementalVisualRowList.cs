namespace Azunyan.Core;

/// <summary>
/// Presents a changed row window together with lazily rebased suffix rows.
/// The old row list remains the source of truth for unaffected row shape.
/// </summary>
internal sealed class IncrementalVisualRowList : IReadOnlyList<VisualRow>
{
    private readonly IReadOnlyList<VisualRow> _previous;
    private readonly IReadOnlyList<VisualRow> _changed;
    private readonly TextProjection _projection;
    private readonly IReadOnlyDictionary<string, BlockAdornment> _blocksById;
    private readonly int _oldStart;
    private readonly int _oldEnd;
    private readonly int _logicalDelta;
    private readonly Dictionary<int, VisualRow> _suffixRows = new();

    public IncrementalVisualRowList(
        IReadOnlyList<VisualRow> previous,
        IReadOnlyList<VisualRow> changed,
        TextProjection projection,
        IReadOnlyDictionary<string, BlockAdornment> blocksById,
        int oldStart,
        int oldEnd,
        int logicalDelta)
    {
        _previous = previous ?? throw new ArgumentNullException(nameof(previous));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        _blocksById = blocksById ?? throw new ArgumentNullException(nameof(blocksById));
        _oldStart = oldStart;
        _oldEnd = oldEnd;
        _logicalDelta = logicalDelta;
        Count = checked(oldStart + changed.Count + previous.Count - oldEnd);
    }

    public int Count { get; }

    public VisualRow this[int index]
    {
        get
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (index < _oldStart)
            {
                return _previous[index];
            }

            var changedEnd = _oldStart + _changed.Count;
            if (index < changedEnd)
            {
                return _changed[index - _oldStart];
            }

            if (_suffixRows.TryGetValue(index, out var row))
            {
                return row;
            }

            var oldIndex = checked(_oldEnd + index - changedEnd);
            row = Rebase(_previous[oldIndex], index);
            _suffixRows[index] = row;
            return row;
        }
    }

    public IEnumerator<VisualRow> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();

    private VisualRow Rebase(VisualRow previous, int visualRow)
    {
        var logicalLine = checked(previous.LogicalLine + _logicalDelta);
        if (previous.TextLine is { } textLine)
        {
            if (!_projection.TryGetVisualLine(logicalLine, out var projectedLine))
            {
                throw new InvalidOperationException("An unaffected text row is hidden by the new projection.");
            }

            return new VisualRow(
                visualRow,
                logicalLine,
                _projection.Lines[projectedLine],
                null,
                previous.TextStartColumn,
                previous.TextLength);
        }

        if (previous.BlockAdornment is not { } previousBlock
            || !_blocksById.TryGetValue(previousBlock.Id, out var block))
        {
            throw new InvalidOperationException("An unaffected block row is missing from the new projection.");
        }

        var blockLine = _projection.MapDocumentPosition(block.Anchor).VisualLine;
        return new VisualRow(
            visualRow,
            _projection.Lines[blockLine].LogicalLine,
            null,
            block);
    }
}
