namespace Azunyan.Core;

/// <summary>
/// A half-open range in a text snapshot. Positions and lengths are UTF-16 code
/// units, matching <see cref="string"/> and the text controls used by the UI.
/// </summary>
public readonly record struct TextRange
{
    public TextRange(int start, int length)
    {
        if (start < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        _ = checked(start + length);
        Start = start;
        Length = length;
    }

    public int Start { get; }

    public int Length { get; }

    public int End => checked(Start + Length);

    public bool IsEmpty => Length == 0;

    public static TextRange Empty(int position) => new(position, 0);

    public static TextRange FromBounds(int start, int end)
    {
        if (end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "The end must not precede the start.");
        }

        return new TextRange(start, end - start);
    }

    public bool Contains(int position) => position >= Start && position < End;

    public bool Contains(TextRange range) => range.Start >= Start && range.End <= End;

    public override string ToString() => $"[{Start}..{End})";
}
