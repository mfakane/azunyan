using System.Globalization;

namespace Azunyan.Core;

/// <summary>
/// Navigation helpers for text positions. Positions remain UTF-16 offsets so
/// they can be passed directly to WinUI text controls, while movement and
/// deletion can respect Unicode scalar values and extended grapheme clusters.
/// </summary>
public static class UnicodeText
{
    public static int GetScalarLengthAt(string text, int position)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidatePosition(text, position, allowEnd: false);

        return char.IsHighSurrogate(text[position])
            && position + 1 < text.Length
            && char.IsLowSurrogate(text[position + 1])
            ? 2
            : 1;
    }

    public static int GetNextScalarPosition(string text, int position)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidatePosition(text, position, allowEnd: true);
        return position == text.Length ? position : position + GetScalarLengthAt(text, position);
    }

    public static int GetPreviousScalarPosition(string text, int position)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidatePosition(text, position, allowEnd: true);
        if (position == 0)
        {
            return 0;
        }

        return position > 1
            && char.IsLowSurrogate(text[position - 1])
            && char.IsHighSurrogate(text[position - 2])
            ? position - 2
            : position - 1;
    }

    public static bool IsScalarBoundary(string text, int position)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidatePosition(text, position, allowEnd: true);
        return position == 0
            || position == text.Length
            || !char.IsLowSurrogate(text[position]);
    }

    public static int[] GetTextElementStarts(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return StringInfo.ParseCombiningCharacters(text);
    }

    public static int GetTextElementCount(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return StringInfo.ParseCombiningCharacters(text).Length;
    }

    public static TextRange GetTextElementRange(string text, int position)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidatePosition(text, position, allowEnd: false);

        var starts = StringInfo.ParseCombiningCharacters(text);
        var element = FindElementAt(starts, position);
        var end = element + 1 < starts.Length ? starts[element + 1] : text.Length;
        return TextRange.FromBounds(starts[element], end);
    }

    public static bool IsTextElementBoundary(string text, int position)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidatePosition(text, position, allowEnd: true);
        if (position == 0 || position == text.Length)
        {
            return true;
        }

        var starts = StringInfo.ParseCombiningCharacters(text);
        return Array.BinarySearch(starts, position) >= 0;
    }

    public static int GetNextTextElementPosition(string text, int position)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidatePosition(text, position, allowEnd: true);
        if (position == text.Length)
        {
            return position;
        }

        var starts = StringInfo.ParseCombiningCharacters(text);
        var element = FindElementAt(starts, position);
        return element + 1 < starts.Length ? starts[element + 1] : text.Length;
    }

    public static int GetPreviousTextElementPosition(string text, int position)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidatePosition(text, position, allowEnd: true);
        if (position == 0)
        {
            return 0;
        }

        var starts = StringInfo.ParseCombiningCharacters(text);
        var element = FindElementAt(starts, position - 1);
        return starts[element];
    }

    public static int MoveByScalars(string text, int position, int count)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidatePosition(text, position, allowEnd: true);

        while (count > 0 && position < text.Length)
        {
            position = GetNextScalarPosition(text, position);
            count--;
        }

        while (count < 0 && position > 0)
        {
            position = GetPreviousScalarPosition(text, position);
            count++;
        }

        return position;
    }

    public static int MoveByTextElements(string text, int position, int count)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidatePosition(text, position, allowEnd: true);

        while (count > 0 && position < text.Length)
        {
            position = GetNextTextElementPosition(text, position);
            count--;
        }

        while (count < 0 && position > 0)
        {
            position = GetPreviousTextElementPosition(text, position);
            count++;
        }

        return position;
    }

    public static int MoveByWord(string text, int position, int count)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidatePosition(text, position, allowEnd: true);

        while (count > 0 && position < text.Length)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position]))
            {
                position = GetNextTextElementPosition(text, position);
            }

            var word = IsWordCharacter(text, position);
            while (position < text.Length
                && IsWordCharacter(text, position) == word
                && !char.IsWhiteSpace(text[position]))
            {
                position = GetNextTextElementPosition(text, position);
            }

            count--;
        }

        while (count < 0 && position > 0)
        {
            position = GetPreviousTextElementPosition(text, position);
            while (position > 0 && char.IsWhiteSpace(text[position]))
            {
                position = GetPreviousTextElementPosition(text, position);
            }

            var word = IsWordCharacter(text, position);
            while (position > 0)
            {
                var previous = GetPreviousTextElementPosition(text, position);
                if (char.IsWhiteSpace(text[previous])
                    || IsWordCharacter(text, previous) != word)
                {
                    break;
                }

                position = previous;
            }

            count++;
        }

        return position;
    }

    public static TextRange GetWordRange(string text, int position)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidatePosition(text, position, allowEnd: false);

        var element = GetTextElementRange(text, position);
        if (char.IsWhiteSpace(text[element.Start]))
        {
            return TextRange.Empty(element.Start);
        }

        var word = IsWordCharacter(text, element.Start);
        var start = element.Start;
        while (start > 0)
        {
            var previous = GetPreviousTextElementPosition(text, start);
            if (char.IsWhiteSpace(text[previous])
                || IsWordCharacter(text, previous) != word)
            {
                break;
            }

            start = previous;
        }

        var end = element.End;
        while (end < text.Length
            && !char.IsWhiteSpace(text[end])
            && IsWordCharacter(text, end) == word)
        {
            end = GetNextTextElementPosition(text, end);
        }

        return TextRange.FromBounds(start, end);
    }

    public static TextRange GetBackwardDeleteRange(string text, int position)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidatePosition(text, position, allowEnd: true);
        if (position == 0)
        {
            return TextRange.Empty(0);
        }

        var element = GetTextElementRange(text, position - 1);
        return TextRange.FromBounds(element.Start, Math.Max(position, element.End));
    }

    public static TextRange GetForwardDeleteRange(string text, int position)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidatePosition(text, position, allowEnd: true);
        if (position == text.Length)
        {
            return TextRange.Empty(position);
        }

        var element = GetTextElementRange(text, position);
        return element;
    }

    public static TextRange GetBackwardScalarDeleteRange(string text, int position)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidatePosition(text, position, allowEnd: true);
        if (position == 0)
        {
            return TextRange.Empty(0);
        }

        var start = GetPreviousScalarPosition(text, position);
        var end = IsScalarBoundary(text, position)
            ? position
            : GetNextScalarPosition(text, position);
        return TextRange.FromBounds(start, end);
    }

    public static TextRange GetForwardScalarDeleteRange(string text, int position)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidatePosition(text, position, allowEnd: true);
        if (position == text.Length)
        {
            return TextRange.Empty(position);
        }

        var start = IsScalarBoundary(text, position)
            ? position
            : GetPreviousScalarPosition(text, position);
        var end = GetNextScalarPosition(text, position);
        return TextRange.FromBounds(start, end);
    }

    private static int FindElementAt(int[] starts, int position)
    {
        var low = 0;
        var high = starts.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (starts[middle] <= position)
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

    private static void ValidatePosition(string text, int position, bool allowEnd)
    {
        var upperBound = allowEnd ? text.Length : text.Length - 1;
        if (position < 0 || position > upperBound)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }
    }

    private static bool IsWordCharacter(string text, int position) =>
        position < text.Length
        && (char.IsLetterOrDigit(text[position])
            || char.GetUnicodeCategory(text[position]) is
                UnicodeCategory.ConnectorPunctuation
                or UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark);
}
