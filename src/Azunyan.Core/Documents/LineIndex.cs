namespace Azunyan.Core;

/// <summary>
/// Lazily-built line metadata for a snapshot. Lines and columns are zero
/// based. Newline sequences are treated as one delimiter, including CRLF.
/// </summary>
public sealed class LineIndex
{
    private readonly TextTree _tree;
    private readonly int _lineCount;

    internal LineIndex(TextSnapshot snapshot)
    {
        _tree = snapshot.Tree;
        _lineCount = _tree.GetLineBreakCountBefore(_tree.Length) + 1;
    }

    public int LineCount => _lineCount;

    public int GetLineStart(int line)
    {
        ValidateLine(line);
        return line == 0 ? 0 : GetBreak(line - 1).End;
    }

    public int GetLineEnd(int line)
    {
        ValidateLine(line);
        return line == _lineCount - 1 ? _tree.Length : GetBreak(line).Start;
    }

    public int GetLineLength(int line)
    {
        ValidateLine(line);
        return GetLineEnd(line) - GetLineStart(line);
    }

    public TextRange GetLineRange(int line)
    {
        ValidateLine(line);
        return TextRange.FromBounds(GetLineStart(line), GetLineEnd(line));
    }

    public LineColumn GetLineColumn(int position)
    {
        if (position < 0 || position > _tree.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        var line = _tree.GetLineBreakCountBefore(position);
        return new LineColumn(line, position - GetLineStart(line));
    }

    public int GetPosition(LineColumn lineColumn)
    {
        ValidateLine(lineColumn.Line);
        if (lineColumn.Column > GetLineLength(lineColumn.Line))
        {
            throw new ArgumentOutOfRangeException(nameof(lineColumn), "The column is past the end of the line.");
        }

        return GetLineStart(lineColumn.Line) + lineColumn.Column;
    }

    public int GetLine(int position) => GetLineColumn(position).Line;

    public int GetColumn(int position) => GetLineColumn(position).Column;

    private (int Start, int End) GetBreak(int index)
    {
        var breakInfo = _tree.GetLineBreak(index);
        return (breakInfo.Start, breakInfo.Start + breakInfo.Length);
    }

    private void ValidateLine(int line)
    {
        if (line < 0 || line >= _lineCount)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }
    }
}
