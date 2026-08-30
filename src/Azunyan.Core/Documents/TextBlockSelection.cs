namespace Azunyan.Core;

/// <summary>
/// A position used by a block selection. Column is a display column rather
/// than a UTF-16 offset, so tabs occupy the display cells up to their next
/// tab stop.
/// </summary>
public readonly record struct TextBlockPosition
{
    public TextBlockPosition(int line, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfNegative(column);

        Line = line;
        Column = column;
    }

    public int Line { get; }

    public int Column { get; }
}

/// <summary>
/// A rectangular selection expressed by its two display-space corners.
/// </summary>
public readonly record struct TextBlockSelection
{
    public TextBlockSelection(TextBlockPosition anchor, TextBlockPosition active)
    {
        Anchor = anchor;
        Active = active;
    }

    public TextBlockSelection(
        int anchorLine,
        int anchorColumn,
        int activeLine,
        int activeColumn)
        : this(
            new TextBlockPosition(anchorLine, anchorColumn),
            new TextBlockPosition(activeLine, activeColumn))
    {
    }

    public TextBlockPosition Anchor { get; }

    public TextBlockPosition Active { get; }

    public int TopLine => Math.Min(Anchor.Line, Active.Line);

    public int BottomLine => Math.Max(Anchor.Line, Active.Line);

    public int LeftColumn => Math.Min(Anchor.Column, Active.Column);

    public int RightColumn => Math.Max(Anchor.Column, Active.Column);

    public bool IsEmpty => Anchor == Active;

    public override string ToString() =>
        IsEmpty
            ? $"BlockCaret({Active.Line}:{Active.Column})"
            : $"BlockSelection({TopLine}:{LeftColumn}..{BottomLine}:{RightColumn})";
}
