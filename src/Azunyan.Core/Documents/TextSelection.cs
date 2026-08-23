namespace Azunyan.Core;

/// <summary>
/// The active selection in a document. <see cref="Anchor"/> is the fixed end
/// and <see cref="Active"/> is the caret end, so a selection can retain its
/// direction while exposing a normalized range.
/// </summary>
public readonly record struct TextSelection
{
    public TextSelection(int anchor, int active)
    {
        if (anchor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(anchor));
        }

        if (active < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(active));
        }

        Anchor = anchor;
        Active = active;
    }

    public int Anchor { get; }

    public int Active { get; }

    public int Start => Math.Min(Anchor, Active);

    public int End => Math.Max(Anchor, Active);

    public int Length => End - Start;

    public int CaretPosition => Active;

    public bool IsEmpty => Anchor == Active;

    public bool IsReversed => Active < Anchor;

    public TextRange Range => new(Start, Length);

    public static TextSelection Caret(int position) => new(position, position);

    public TextSelection CollapseTo(int position) => Caret(position);

    public override string ToString() => IsEmpty
        ? $"Caret({CaretPosition})"
        : $"Selection({Start}..{End}, caret={CaretPosition})";
}

/// <summary>
/// A named caret value for consumers that do not need an anchor/active
/// selection pair.
/// </summary>
public readonly record struct TextCaret
{
    public TextCaret(int position)
    {
        if (position < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        Position = position;
    }

    public int Position { get; }
}

/// <summary>
/// A zero-based line and column pair.
/// </summary>
public readonly record struct LineColumn
{
    public LineColumn(int line, int column)
    {
        if (line < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }

        if (column < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        Line = line;
        Column = column;
    }

    public int Line { get; }

    public int Column { get; }
}
