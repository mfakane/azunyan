namespace Azunyan.Core;

/// <summary>
/// Lazily-built line metadata for a snapshot. Lines and columns are zero
/// based. Newline sequences are treated as one delimiter, including CRLF.
/// </summary>
public sealed class LineIndex
{
    private readonly int[] _starts;
    private readonly int[] _ends;
    private readonly TextSnapshot _snapshot;

    internal LineIndex(TextSnapshot snapshot)
    {
        _snapshot = snapshot;
        var starts = new List<int> { 0 };
        var ends = new List<int>();
        var position = 0;
        var pendingCarriageReturn = -1;
        snapshot.Tree.VisitPieces(piece =>
        {
            foreach (var character in piece.Span)
            {
                if (pendingCarriageReturn >= 0)
                {
                    if (character == '\n')
                    {
                        position++;
                        ends.Add(pendingCarriageReturn);
                        starts.Add(position);
                        pendingCarriageReturn = -1;
                        continue;
                    }

                    ends.Add(pendingCarriageReturn);
                    starts.Add(position);
                    pendingCarriageReturn = -1;
                }

                switch (character)
                {
                    case '\r':
                        pendingCarriageReturn = position;
                        position++;
                        break;
                    case '\n':
                        ends.Add(position);
                        position++;
                        starts.Add(position);
                        break;
                    default:
                        position++;
                        break;
                }
            }
        });

        if (pendingCarriageReturn >= 0)
        {
            ends.Add(pendingCarriageReturn);
            starts.Add(position);
        }

        ends.Add(snapshot.Length);
        _starts = starts.ToArray();
        _ends = ends.ToArray();
    }

    public int LineCount => _starts.Length;

    public int GetLineStart(int line)
    {
        ValidateLine(line);
        return _starts[line];
    }

    public int GetLineEnd(int line)
    {
        ValidateLine(line);
        return _ends[line];
    }

    public int GetLineLength(int line)
    {
        ValidateLine(line);
        return _ends[line] - _starts[line];
    }

    public TextRange GetLineRange(int line)
    {
        ValidateLine(line);
        return TextRange.FromBounds(_starts[line], _ends[line]);
    }

    public LineColumn GetLineColumn(int position)
    {
        if (position < 0 || position > _snapshot.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        var line = FindLine(position);
        return new LineColumn(line, position - _starts[line]);
    }

    public int GetPosition(LineColumn lineColumn)
    {
        ValidateLine(lineColumn.Line);
        if (lineColumn.Column > GetLineLength(lineColumn.Line))
        {
            throw new ArgumentOutOfRangeException(nameof(lineColumn), "The column is past the end of the line.");
        }

        return _starts[lineColumn.Line] + lineColumn.Column;
    }

    public int GetLine(int position) => GetLineColumn(position).Line;

    public int GetColumn(int position) => GetLineColumn(position).Column;

    private int FindLine(int position)
    {
        var low = 0;
        var high = _starts.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (_starts[middle] <= position)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return Math.Max(0, high);
    }

    private void ValidateLine(int line)
    {
        if (line < 0 || line >= _starts.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }
    }
}
