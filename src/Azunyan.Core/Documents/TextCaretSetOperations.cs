namespace Azunyan.Core;

public readonly record struct TextCaretEdit(
    TextRange Range,
    string Replacement,
    TextCaretSet CaretSet);

/// <summary>
/// Pure operations for moving and editing an immutable caret collection.
/// Applying the returned edit through Document.Replace keeps the whole action
/// in one document change and one undo record.
/// </summary>
public static class TextCaretSetOperations
{
    public static TextCaretSet FromBlockSelection(
        TextSnapshot snapshot,
        TextBlockSelection selection,
        int tabDisplaySize)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfLessThan(tabDisplaySize, 1);

        var firstLine = Math.Clamp(selection.TopLine, 0, snapshot.Lines.LineCount - 1);
        var lastLine = Math.Clamp(selection.BottomLine, firstLine, snapshot.Lines.LineCount - 1);
        var carets = new List<TextCaretState>(lastLine - firstLine + 1);
        var primaryPosition = TextBlockSelectionOperations.GetCaretPosition(
            snapshot,
            selection.Active,
            tabDisplaySize);

        for (var line = firstLine; line <= lastLine; line++)
        {
            var lineRange = TextBlockSelectionOperations.GetLineRange(
                snapshot,
                line,
                selection.LeftColumn,
                selection.RightColumn,
                tabDisplaySize);
            var lineSelection = lineRange.IsEmpty
                ? TextSelection.Caret(lineRange.Start)
                : selection.Active.Column >= selection.Anchor.Column
                    ? new TextSelection(lineRange.Start, lineRange.End)
                    : new TextSelection(lineRange.End, lineRange.Start);
            if (carets.Any(caret => caret.CaretPosition == lineSelection.CaretPosition))
            {
                continue;
            }

            carets.Add(new TextCaretState(
                lineSelection,
                TextBlockSelectionOperations.GetDisplayColumn(
                    snapshot,
                    lineSelection.CaretPosition,
                    tabDisplaySize)));
        }

        var primaryIndex = carets.FindIndex(caret => caret.CaretPosition == primaryPosition);
        if (primaryIndex < 0)
        {
            primaryIndex = Math.Clamp(
                selection.Active.Line - firstLine,
                0,
                Math.Max(0, carets.Count - 1));
        }

        return new TextCaretSet(carets, primaryIndex);
    }

    public static TextCaretSet FromVisualBlockSelection(
        TextSnapshot snapshot,
        TextBlockSelection selection,
        VisualRowMap rows,
        int tabDisplaySize)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentOutOfRangeException.ThrowIfLessThan(tabDisplaySize, 1);
        if (selection.CoordinateSpace != TextBlockSelectionCoordinateSpace.VisualRows)
        {
            throw new ArgumentException(
                "The selection must use visual-row coordinates.",
                nameof(selection));
        }

        if (rows.Rows.Count == 0)
        {
            throw new ArgumentException("The visual row map cannot be empty.", nameof(rows));
        }

        var firstRow = Math.Clamp(selection.TopLine, 0, rows.Rows.Count - 1);
        var lastRow = Math.Clamp(
            selection.BottomLine,
            firstRow,
            rows.Rows.Count - 1);
        var carets = new List<TextCaretState>(lastRow - firstRow + 1);
        var primaryIndex = -1;
        for (var rowIndex = firstRow; rowIndex <= lastRow; rowIndex++)
        {
            var row = rows.Rows[rowIndex];
            if (row.TextLine is not { } textLine)
            {
                continue;
            }

            var leftColumn = Math.Clamp(selection.LeftColumn, 0, row.TextLength);
            var rightColumn = Math.Clamp(selection.RightColumn, 0, row.TextLength);
            var start = textLine.GetAnchor(row.TextStartColumn + leftColumn)
                .Position.Offset;
            var end = textLine.GetAnchor(row.TextStartColumn + rightColumn)
                .Position.Offset;
            var rowSelection = start == end
                ? TextSelection.Caret(start)
                : selection.Active.Column >= selection.Anchor.Column
                    ? new TextSelection(start, end)
                    : new TextSelection(end, start);
            carets.Add(new TextCaretState(
                rowSelection,
                TextBlockSelectionOperations.GetDisplayColumn(
                    snapshot,
                    rowSelection.CaretPosition,
                    tabDisplaySize)));

            if (row.VisualRowIndex == selection.Active.Line)
            {
                primaryIndex = carets.Count - 1;
            }
        }

        if (carets.Count == 0)
        {
            throw new ArgumentException(
                "The selection does not contain a text row.",
                nameof(selection));
        }

        if (primaryIndex < 0)
        {
            primaryIndex = FindNearestVisualRowIndex(
                rows,
                firstRow,
                lastRow,
                selection.Active.Line,
                carets);
        }

        return new TextCaretSet(carets, primaryIndex);
    }

    public static TextCaretSet MoveHorizontal(
        TextSnapshot snapshot,
        TextCaretSet carets,
        int count,
        bool extendSelection,
        bool byWord = false,
        int tabDisplaySize = 4)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(carets);
        if (count == 0)
        {
            return carets;
        }

        var result = new List<TextCaretState>(carets.Count);
        foreach (var caret in carets)
        {
            var selection = caret.Selection;
            var position = extendSelection || selection.IsEmpty
                ? selection.CaretPosition
                : count < 0 ? selection.Start : selection.End;
            var target = byWord
                ? UnicodeText.MoveByWord(snapshot.Text, position, count)
                : UnicodeText.MoveByTextElements(snapshot.Text, position, count);
            var nextSelection = extendSelection
                ? new TextSelection(selection.Anchor, target)
                : TextSelection.Caret(target);
            result.Add(new TextCaretState(
                nextSelection,
                TextBlockSelectionOperations.GetDisplayColumn(snapshot, target, tabDisplaySize)));
        }

        return Deduplicate(result, carets.PrimaryIndex);
    }

    public static TextCaretSet MoveVertical(
        TextSnapshot snapshot,
        TextCaretSet carets,
        int direction,
        int tabDisplaySize,
        bool extendSelection)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(carets);
        if (direction == 0)
        {
            return carets;
        }

        var result = new List<TextCaretState>(carets.Count);
        foreach (var caret in carets)
        {
            var selection = caret.Selection;
            var sourcePosition = extendSelection || selection.IsEmpty
                ? selection.CaretPosition
                : direction < 0 ? selection.Start : selection.End;
            var line = snapshot.Lines.GetLine(sourcePosition);
            var targetLine = Math.Clamp(
                line + Math.Sign(direction),
                0,
                snapshot.Lines.LineCount - 1);
            var target = TextBlockSelectionOperations.GetCaretPosition(
                snapshot,
                new TextBlockPosition(targetLine, caret.PreferredDisplayColumn),
                tabDisplaySize);
            var nextSelection = extendSelection
                ? new TextSelection(selection.Anchor, target)
                : TextSelection.Caret(target);
            result.Add(new TextCaretState(nextSelection, caret.PreferredDisplayColumn));
        }

        return Deduplicate(result, carets.PrimaryIndex);
    }

    public static TextCaretSet MoveToLineBoundary(
        TextSnapshot snapshot,
        TextCaretSet carets,
        bool end,
        bool documentBoundary,
        bool extendSelection,
        int tabDisplaySize)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(carets);

        var result = new List<TextCaretState>(carets.Count);
        foreach (var caret in carets)
        {
            var selection = caret.Selection;
            var sourcePosition = extendSelection || selection.IsEmpty
                ? selection.CaretPosition
                : end ? selection.End : selection.Start;
            var target = documentBoundary
                ? end ? snapshot.Length : 0
                : GetLineBoundary(snapshot, sourcePosition, end);
            var nextSelection = extendSelection
                ? new TextSelection(selection.Anchor, target)
                : TextSelection.Caret(target);
            result.Add(new TextCaretState(
                nextSelection,
                TextBlockSelectionOperations.GetDisplayColumn(snapshot, target, tabDisplaySize)));
        }

        return Deduplicate(result, carets.PrimaryIndex);
    }

    public static TextCaretEdit CreateReplacement(
        TextSnapshot snapshot,
        TextCaretSet carets,
        IReadOnlyList<string?> replacements,
        int tabDisplaySize)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(carets);
        ArgumentNullException.ThrowIfNull(replacements);
        if (replacements.Count != carets.Count)
        {
            throw new ArgumentException("One replacement is required per caret.", nameof(replacements));
        }

        var operations = new List<ReplacementOperation>();
        for (var index = 0; index < carets.Count; index++)
        {
            if (replacements[index] is not { } replacement)
            {
                continue;
            }

            operations.Add(new ReplacementOperation(
                index,
                carets[index].Selection.Range,
                replacement));
        }

        return BuildEdit(snapshot, carets, operations, tabDisplaySize);
    }

    public static TextCaretEdit CreateDeletion(
        TextSnapshot snapshot,
        TextCaretSet carets,
        bool backward,
        int tabDisplaySize)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(carets);

        var operations = new List<ReplacementOperation>();
        for (var index = 0; index < carets.Count; index++)
        {
            var selection = carets[index].Selection;
            var range = selection.IsEmpty
                ? backward
                    ? UnicodeText.GetBackwardDeleteRange(snapshot.Text, selection.CaretPosition)
                    : UnicodeText.GetForwardDeleteRange(snapshot.Text, selection.CaretPosition)
                : selection.Range;
            range = NormalizeLineEndingRange(snapshot.Text, range);
            operations.Add(new ReplacementOperation(index, range, string.Empty));
        }

        return BuildEdit(snapshot, carets, operations, tabDisplaySize);
    }

    public static TextCaretEdit CreatePaste(
        TextSnapshot snapshot,
        TextCaretSet carets,
        IReadOnlyList<string> lines,
        string lineEnding,
        int tabDisplaySize)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(lineEnding);
        if (lines.Count == 0)
        {
            return CreateReplacement(
                snapshot,
                carets,
                Enumerable.Repeat<string?>(string.Empty, carets.Count).ToArray(),
                tabDisplaySize);
        }

        var replacements = new string?[carets.Count];
        for (var index = 0; index < carets.Count; index++)
        {
            replacements[index] = lines.Count == 1
                ? lines[0]
                : index < lines.Count ? lines[index] : null;
        }

        var edit = CreateReplacement(snapshot, carets, replacements, tabDisplaySize);
        if (lines.Count <= carets.Count)
        {
            return edit;
        }

        var lastLine = snapshot.Lines.GetLine(carets[^1].CaretPosition);
        var insertionPosition = snapshot.Lines.GetLineEnd(lastLine);
        var extra = string.Concat(
            lines.Skip(carets.Count).Select(line => lineEnding + line));
        if (extra.Length == 0)
        {
            return edit;
        }

        var extraRange = TextRange.Empty(insertionPosition);
        var combinedRange = TextRange.FromBounds(
            Math.Min(edit.Range.Start, extraRange.Start),
            Math.Max(edit.Range.End, extraRange.End));
        var original = snapshot.GetText(combinedRange);
        var replacement = original
            .Insert(extraRange.Start - combinedRange.Start, extra);
        if (edit.Range.Start >= combinedRange.Start && edit.Range.End <= combinedRange.End)
        {
            replacement = replacement.Remove(
                edit.Range.Start - combinedRange.Start,
                edit.Range.Length)
                .Insert(edit.Range.Start - combinedRange.Start, edit.Replacement);
        }

        return new TextCaretEdit(
            combinedRange,
            replacement,
            edit.CaretSet);
    }

    private static TextCaretEdit BuildEdit(
        TextSnapshot snapshot,
        TextCaretSet carets,
        IReadOnlyList<ReplacementOperation> sourceOperations,
        int tabDisplaySize)
    {
        if (sourceOperations.Count == 0)
        {
            return new TextCaretEdit(
                TextRange.Empty(carets.Primary.CaretPosition),
                string.Empty,
                carets);
        }

        var operations = MergeOperations(sourceOperations);
        var start = operations.Min(operation => operation.Range.Start);
        var end = operations.Max(operation => operation.Range.End);
        var range = TextRange.FromBounds(start, end);
        var oldText = snapshot.GetText(range);
        var builder = new System.Text.StringBuilder();
        var cursor = start;
        var outputCaretPositions = new int[operations.Count];
        foreach (var operation in operations)
        {
            builder.Append(snapshot.Text[cursor..operation.Range.Start]);
            builder.Append(operation.Replacement);
            cursor = operation.Range.End;
            outputCaretPositions[operations.IndexOf(operation)] =
                start + builder.Length;
        }

        builder.Append(snapshot.Text[cursor..end]);
        var replacement = builder.ToString();
        var newText = snapshot.Text[..start] + replacement + snapshot.Text[end..];

        var result = new List<TextCaretState>(carets.Count);
        var primaryIndex = 0;
        for (var index = 0; index < carets.Count; index++)
        {
            var operation = operations.FirstOrDefault(candidate => candidate.Indices.Contains(index));
            var position = operation is not null
                ? outputCaretPositions[operations.IndexOf(operation)]
                : MapPosition(carets[index].CaretPosition, operations);
            var selection = TextSelection.Caret(position);
            result.Add(new TextCaretState(
                selection,
                TextBlockSelectionOperations.GetDisplayColumn(
                    new TextSnapshot(newText),
                    position,
                    tabDisplaySize)));
            if (index == carets.PrimaryIndex)
            {
                primaryIndex = result.Count - 1;
            }
        }

        return new TextCaretEdit(range, replacement, Deduplicate(result, primaryIndex));
    }

    private static List<ReplacementOperation> MergeOperations(
        IReadOnlyList<ReplacementOperation> source)
    {
        var sorted = source
            .OrderBy(operation => operation.Range.Start)
            .ThenBy(operation => operation.Range.End)
            .ToArray();
        var result = new List<ReplacementOperation>();
        foreach (var operation in sorted)
        {
            if (result.Count == 0 || !Overlaps(result[^1].Range, operation.Range))
            {
                result.Add(operation with { Indices = new List<int> { operation.Index } });
                continue;
            }

            var current = result[^1];
            var end = Math.Max(current.Range.End, operation.Range.End);
            current.Indices.Add(operation.Index);
            result[^1] = current with {
                Range = TextRange.FromBounds(current.Range.Start, end)
            };
        }

        return result;
    }

    private static int FindNearestVisualRowIndex(
        VisualRowMap rows,
        int firstRow,
        int lastRow,
        int activeRow,
        List<TextCaretState> carets)
    {
        var nearest = -1;
        var nearestDistance = int.MaxValue;
        var caretIndex = 0;
        for (var rowIndex = firstRow; rowIndex <= lastRow; rowIndex++)
        {
            if (rows.Rows[rowIndex].TextLine is null)
            {
                continue;
            }

            var distance = Math.Abs(rows.Rows[rowIndex].VisualRowIndex - activeRow);
            if (distance < nearestDistance)
            {
                nearest = caretIndex;
                nearestDistance = distance;
            }

            caretIndex++;
        }

        return Math.Clamp(nearest, 0, carets.Count - 1);
    }

    private static bool Overlaps(TextRange left, TextRange right)
    {
        if (left.IsEmpty || right.IsEmpty)
        {
            return left.IsEmpty
                && right.IsEmpty
                && left.Start == right.Start;
        }

        return right.Start < left.End && left.Start < right.End;
    }

    private static int MapPosition(int position, IReadOnlyList<ReplacementOperation> operations)
    {
        var mapped = position;
        foreach (var operation in operations)
        {
            if (position < operation.Range.Start)
            {
                break;
            }

            if (position <= operation.Range.End)
            {
                return operation.Range.Start + operation.Replacement.Length;
            }

            mapped += operation.Replacement.Length - operation.Range.Length;
        }

        return mapped;
    }

    private static TextCaretSet Deduplicate(
        List<TextCaretState> source,
        int primaryIndex)
    {
        var result = new List<TextCaretState>(source.Count);
        var primaryResult = 0;
        for (var index = 0; index < source.Count; index++)
        {
            var existing = result.FindIndex(caret => caret.CaretPosition == source[index].CaretPosition);
            if (existing >= 0)
            {
                if (index == primaryIndex)
                {
                    primaryResult = existing;
                }

                continue;
            }

            if (index == primaryIndex)
            {
                primaryResult = result.Count;
            }

            result.Add(source[index]);
        }

        return new TextCaretSet(result, Math.Clamp(primaryResult, 0, Math.Max(0, result.Count - 1)));
    }

    private static int GetLineBoundary(TextSnapshot snapshot, int position, bool end)
    {
        var line = snapshot.Lines.GetLine(Math.Clamp(position, 0, snapshot.Length));
        return end
            ? snapshot.Lines.GetLineEnd(line)
            : snapshot.Lines.GetLineStart(line);
    }

    private static TextRange NormalizeLineEndingRange(string text, TextRange range)
    {
        if (range.IsEmpty)
        {
            return range;
        }

        var start = range.Start;
        var end = range.End;
        if (start > 0 && start < text.Length && text[start] == '\n' && text[start - 1] == '\r')
        {
            start--;
        }

        if (end < text.Length && text[end - 1] == '\r' && text[end] == '\n')
        {
            end++;
        }

        return TextRange.FromBounds(start, end);
    }

    private sealed record ReplacementOperation(
        int Index,
        TextRange Range,
        string Replacement)
    {
        public List<int> Indices { get; init; } = new();
    }
}
