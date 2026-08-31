namespace Azunyan.Core;

/// <summary>
/// The coordinate space used by a rectangular selection.
/// </summary>
public enum TextBlockSelectionCoordinateSpace
{
    /// <summary>
    /// Lines in the document snapshot. This is the traditional block-selection
    /// coordinate space used when wrapping is disabled.
    /// </summary>
    LogicalLines,

    /// <summary>
    /// Rows after projection and soft wrapping. Line numbers are visual-row
    /// indices and columns are local display columns within each row.
    /// </summary>
    VisualRows
}

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
    public TextBlockSelection(
        TextBlockPosition anchor,
        TextBlockPosition active,
        TextBlockSelectionCoordinateSpace coordinateSpace =
            TextBlockSelectionCoordinateSpace.LogicalLines)
    {
        Anchor = anchor;
        Active = active;
        CoordinateSpace = coordinateSpace;
    }

    public TextBlockSelection(
        int anchorLine,
        int anchorColumn,
        int activeLine,
        int activeColumn,
        TextBlockSelectionCoordinateSpace coordinateSpace =
            TextBlockSelectionCoordinateSpace.LogicalLines)
        : this(
            new TextBlockPosition(anchorLine, anchorColumn),
            new TextBlockPosition(activeLine, activeColumn),
            coordinateSpace)
    {
    }

    public TextBlockPosition Anchor { get; }

    public TextBlockPosition Active { get; }

    public TextBlockSelectionCoordinateSpace CoordinateSpace { get; }

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
