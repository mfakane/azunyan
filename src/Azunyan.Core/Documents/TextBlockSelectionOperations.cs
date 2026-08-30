namespace Azunyan.Core;

public readonly record struct TextBlockEdit
{
    public TextBlockEdit(TextRange range, string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        Range = range;
        Replacement = replacement;
    }

    public TextRange Range { get; }

    public string Replacement { get; }
}

/// <summary>
/// Converts display-space block selections into document ranges and text.
/// The operations deliberately clamp to actual line contents; virtual space
/// is only retained by the selection geometry.
/// </summary>
public static class TextBlockSelectionOperations
{
    public static TextRange GetLineRange(
        TextSnapshot snapshot,
        int line,
        int leftColumn,
        int rightColumn,
        int tabDisplaySize)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(leftColumn);
        ArgumentOutOfRangeException.ThrowIfNegative(rightColumn);
        ArgumentOutOfRangeException.ThrowIfLessThan(tabDisplaySize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rightColumn, leftColumn);

        if (line < 0 || line >= snapshot.Lines.LineCount)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }

        var lineRange = snapshot.Lines.GetLineRange(line);
        var text = snapshot.GetText(lineRange);
        var start = -1;
        var end = -1;
        var displayColumn = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var nextDisplayColumn = GetNextDisplayColumn(
                displayColumn,
                text[index],
                tabDisplaySize);
            if (nextDisplayColumn > leftColumn && displayColumn < rightColumn)
            {
                start = start < 0 ? index : start;
                end = index + 1;
            }

            displayColumn = nextDisplayColumn;
        }

        if (start >= 0)
        {
            return new TextRange(lineRange.Start + start, end - start);
        }

        return TextRange.Empty(
            lineRange.Start + GetTextIndexAtDisplayColumn(
                text,
                leftColumn,
                tabDisplaySize));
    }

    public static IReadOnlyList<TextRange> GetLineRanges(
        TextSnapshot snapshot,
        TextBlockSelection selection,
        int tabDisplaySize,
        int? bottomLine = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfLessThan(tabDisplaySize, 1);

        var firstLine = Math.Clamp(
            selection.TopLine,
            0,
            Math.Max(0, snapshot.Lines.LineCount - 1));
        var requestedBottom = bottomLine ?? selection.BottomLine;
        var lastLine = Math.Clamp(
            Math.Max(firstLine, requestedBottom),
            firstLine,
            Math.Max(0, snapshot.Lines.LineCount - 1));
        var result = new List<TextRange>(lastLine - firstLine + 1);
        for (var line = firstLine; line <= lastLine; line++)
        {
            result.Add(GetLineRange(
                snapshot,
                line,
                selection.LeftColumn,
                selection.RightColumn,
                tabDisplaySize));
        }

        return result;
    }

    public static TextBlockEdit? CreateReplacement(
        TextSnapshot snapshot,
        TextBlockSelection selection,
        int tabDisplaySize,
        IReadOnlyList<string> replacementLines,
        bool repeatSingleLine,
        string lineEnding)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(replacementLines);
        ArgumentNullException.ThrowIfNull(lineEnding);
        ArgumentOutOfRangeException.ThrowIfLessThan(tabDisplaySize, 1);
        if (replacementLines.Count == 0)
        {
            return null;
        }

        var repeats = repeatSingleLine && replacementLines.Count == 1;
        var targetBottom = repeats
            ? selection.BottomLine
            : Math.Max(
                selection.BottomLine,
                checked(selection.TopLine + replacementLines.Count - 1));
        var existingRanges = GetLineRanges(
            snapshot,
            selection,
            tabDisplaySize,
            targetBottom);
        if (existingRanges.Count == 0)
        {
            return null;
        }

        var start = existingRanges.Min(range => range.Start);
        var end = existingRanges.Max(range => range.End);
        var range = TextRange.FromBounds(start, end);
        var oldText = snapshot.GetText(range);
        var newText = oldText;
        for (var index = existingRanges.Count - 1; index >= 0; index--)
        {
            var replacement = GetReplacement(
                replacementLines,
                index,
                repeats);
            if (replacement is null)
            {
                continue;
            }

            var lineRange = existingRanges[index];
            var relativeStart = lineRange.Start - start;
            newText = newText.Remove(relativeStart, lineRange.Length)
                .Insert(relativeStart, replacement);
        }

        var append = new System.Text.StringBuilder();
        var lineCount = snapshot.Lines.LineCount;
        if (!repeats)
        {
            for (var index = 0; index < replacementLines.Count; index++)
            {
                var line = checked(selection.TopLine + index);
                if (line < lineCount)
                {
                    continue;
                }

                append.Append(lineEnding);
                append.Append(replacementLines[index]);
            }
        }

        newText += append.ToString();
        if (string.Equals(oldText, newText, StringComparison.Ordinal))
        {
            return null;
        }

        return new TextBlockEdit(range, newText);
    }

    public static string GetSelectedText(
        TextSnapshot snapshot,
        TextBlockSelection selection,
        int tabDisplaySize,
        string lineEnding)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(lineEnding);

        var ranges = GetLineRanges(snapshot, selection, tabDisplaySize);
        return string.Join(
            lineEnding,
            ranges.Select(snapshot.GetText));
    }

    public static int GetCaretPosition(
        TextSnapshot snapshot,
        TextBlockPosition position,
        int tabDisplaySize)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfLessThan(tabDisplaySize, 1);

        var line = Math.Clamp(
            position.Line,
            0,
            Math.Max(0, snapshot.Lines.LineCount - 1));
        var lineRange = snapshot.Lines.GetLineRange(line);
        var text = snapshot.GetText(lineRange);
        return lineRange.Start + GetTextIndexAtDisplayColumn(
            text,
            position.Column,
            tabDisplaySize);
    }

    public static int GetDisplayColumn(
        TextSnapshot snapshot,
        int position,
        int tabDisplaySize)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfLessThan(tabDisplaySize, 1);

        var line = snapshot.Lines.GetLine(Math.Clamp(position, 0, snapshot.Length));
        var lineRange = snapshot.Lines.GetLineRange(line);
        var local = Math.Clamp(position - lineRange.Start, 0, lineRange.Length);
        var text = snapshot.GetText(lineRange);
        var column = 0;
        for (var index = 0; index < local; index++)
        {
            column = text[index] == '\t'
                ? column + tabDisplaySize - (column % tabDisplaySize)
                : column + 1;
        }

        return column;
    }

    public static string GetPreferredLineEnding(TextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var text = snapshot.Text;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                return index + 1 < text.Length && text[index + 1] == '\n'
                    ? "\r\n"
                    : "\r";
            }

            if (text[index] == '\n')
            {
                return "\n";
            }
        }

        return Environment.NewLine;
    }

    private static int GetTextIndexAtDisplayColumn(
        string text,
        int displayColumn,
        int tabDisplaySize)
    {
        var current = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var next = GetNextDisplayColumn(current, text[index], tabDisplaySize);
            if (displayColumn < next)
            {
                return index;
            }

            current = next;
        }

        return text.Length;
    }

    private static string? GetReplacement(
        IReadOnlyList<string> replacementLines,
        int index,
        bool repeats) =>
        repeats
            ? replacementLines[0]
            : index < replacementLines.Count
                ? replacementLines[index]
                : null;

    private static int GetNextDisplayColumn(
        int current,
        char character,
        int tabDisplaySize) =>
        character == '\t'
            ? checked(current + tabDisplaySize - (current % tabDisplaySize))
            : checked(current + 1);
}
