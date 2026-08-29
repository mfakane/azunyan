namespace Azunyan.Core;

internal sealed class PlainVisualRowList : IReadOnlyList<VisualRow>
{
    private readonly TextProjection _projection;
    private readonly Dictionary<int, VisualRow> _rows = new();

    public PlainVisualRowList(TextProjection projection) => _projection = projection;

    public int Count => _projection.VisualLineCount;

    public VisualRow this[int index]
    {
        get
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (_rows.TryGetValue(index, out var row))
            {
                return row;
            }

            var line = _projection.Lines[index];
            row = new VisualRow(index, line.LogicalLine, line, null);
            _rows[index] = row;
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
}
