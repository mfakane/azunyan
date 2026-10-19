namespace Azunyan.Core;

/// <summary>
/// Pure whole-line operations over an immutable caret collection. Each caret
/// covers the lines its selection touches; a selection ending at the start of
/// a later line does not include that line. Touching or overlapping blocks are
/// merged, and the result is one replacement over the affected lines so that
/// applying it through Document.Replace produces a single undo record.
/// </summary>
public static class TextLineOperations
{
    /// <summary>
    /// Swaps every selected block of lines with the line above or below it.
    /// Returns <see langword="null"/> when a block is already at the document
    /// boundary in that direction.
    /// </summary>
    public static TextCaretEdit? MoveLines(
        TextSnapshot snapshot,
        TextCaretSet carets,
        bool down)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(carets);

        var blocks = GetLineBlocks(snapshot, carets, mergeAdjacent: true);
        if (down ? blocks[^1].Last == snapshot.Lines.LineCount - 1 : blocks[0].First == 0)
        {
            return null;
        }

        var firstLine = down ? blocks[0].First : blocks[0].First - 1;
        var lastLine = down ? blocks[^1].Last + 1 : blocks[^1].Last;
        var order = Enumerable.Range(firstLine, lastLine - firstLine + 1).ToList();
        foreach (var block in blocks)
        {
            if (down)
            {
                order.RemoveAt(block.Last + 1 - firstLine);
                order.Insert(block.First - firstLine, block.Last + 1);
            }
            else
            {
                order.RemoveAt(block.First - 1 - firstLine);
                order.Insert(block.Last - firstLine, block.First - 1);
            }
        }

        var offset = down ? 1 : -1;
        return BuildEdit(
            snapshot,
            carets,
            firstLine,
            lastLine,
            order,
            lineEnding: null,
            line => line - firstLine + offset);
    }

    /// <summary>
    /// Inserts a copy of every selected block of lines. With
    /// <paramref name="down"/> the carets move to the lower copy; otherwise
    /// they stay on the upper copy.
    /// </summary>
    public static TextCaretEdit DuplicateLines(
        TextSnapshot snapshot,
        TextCaretSet carets,
        bool down,
        string? preferredLineEnding)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(carets);

        var blocks = GetLineBlocks(snapshot, carets, mergeAdjacent: false);
        var firstLine = blocks[0].First;
        var lastLine = blocks[^1].Last;
        var order = new List<int>();
        var outputLines = new Dictionary<int, int>();
        var blockIndex = 0;
        for (var line = firstLine; line <= lastLine; line++)
        {
            order.Add(line);
            if (blockIndex >= blocks.Count || line != blocks[blockIndex].Last)
            {
                continue;
            }

            var block = blocks[blockIndex++];
            var copyStart = order.Count;
            var targetStart = down ? copyStart : copyStart - (block.Last - block.First + 1);
            for (var copied = block.First; copied <= block.Last; copied++)
            {
                outputLines[copied] = targetStart + copied - block.First;
                order.Add(copied);
            }
        }

        return BuildEdit(
            snapshot,
            carets,
            firstLine,
            lastLine,
            order,
            TextBlockSelectionOperations.GetPreferredLineEnding(snapshot, preferredLineEnding),
            line => outputLines[line]);
    }

    /// <summary>
    /// Removes every selected block of lines with their line endings. Each
    /// caret moves to the line that takes the place of its block, keeping its
    /// column where the line is long enough.
    /// </summary>
    public static TextCaretEdit DeleteLines(
        TextSnapshot snapshot,
        TextCaretSet carets)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(carets);

        var blocks = GetLineBlocks(snapshot, carets, mergeAdjacent: true);
        var firstLine = blocks[0].First;
        var lastLine = blocks[^1].Last;

        // Include a surviving neighbour so that the line ending between it and
        // the removed lines is removed as well.
        if (lastLine < snapshot.Lines.LineCount - 1)
        {
            lastLine++;
        }
        else if (firstLine > 0)
        {
            firstLine--;
        }

        var deleted = new HashSet<int>(blocks.SelectMany(
            block => Enumerable.Range(block.First, block.Last - block.First + 1)));
        var order = Enumerable
            .Range(firstLine, lastLine - firstLine + 1)
            .Where(line => !deleted.Contains(line))
            .ToList();
        return BuildEdit(
            snapshot,
            carets,
            firstLine,
            lastLine,
            order,
            lineEnding: null,
            line =>
            {
                var survivorsBefore = order.Count(survivor => survivor < line);
                return deleted.Contains(line)
                    ? Math.Min(survivorsBefore, order.Count - 1)
                    : survivorsBefore;
            },
            collapse: true);
    }

    private static List<LineBlock> GetLineBlocks(
        TextSnapshot snapshot,
        TextCaretSet carets,
        bool mergeAdjacent)
    {
        var blocks = carets
            .Select(caret => GetLineBlock(snapshot, caret.Selection))
            .OrderBy(block => block.First)
            .ToList();
        var merged = new List<LineBlock>(blocks.Count);
        foreach (var block in blocks)
        {
            var gap = mergeAdjacent ? 1 : 0;
            if (merged.Count > 0 && block.First <= merged[^1].Last + gap)
            {
                merged[^1] = merged[^1] with { Last = Math.Max(merged[^1].Last, block.Last) };
            }
            else
            {
                merged.Add(block);
            }
        }

        return merged;
    }

    private static LineBlock GetLineBlock(TextSnapshot snapshot, TextSelection selection)
    {
        var first = snapshot.Lines.GetLine(selection.Start);
        var end = snapshot.Lines.GetLineColumn(selection.End);
        var last = !selection.IsEmpty && end.Column == 0 && end.Line > first
            ? end.Line - 1
            : end.Line;
        return new LineBlock(first, last);
    }

    /// <summary>
    /// Replaces lines <paramref name="firstLine"/> through
    /// <paramref name="lastLine"/> with the source lines listed in
    /// <paramref name="order"/>. Line endings stay in place by position;
    /// additional lines use <paramref name="lineEnding"/>. Caret endpoints keep
    /// their column on the output line chosen by <paramref name="outputLine"/>,
    /// and an endpoint that ends a selection at the start of the next line
    /// stays at the start of the line following its block.
    /// </summary>
    private static TextCaretEdit BuildEdit(
        TextSnapshot snapshot,
        TextCaretSet carets,
        int firstLine,
        int lastLine,
        List<int> order,
        string? lineEnding,
        Func<int, int> outputLine,
        bool collapse = false)
    {
        var lines = snapshot.Lines;
        var range = TextRange.FromBounds(lines.GetLineStart(firstLine), lines.GetLineEnd(lastLine));
        var builder = new System.Text.StringBuilder();
        var outputStarts = new int[order.Count];
        for (var index = 0; index < order.Count; index++)
        {
            if (index > 0)
            {
                var separatorLine = firstLine + index - 1;
                builder.Append(separatorLine < lastLine
                    ? snapshot.GetText(TextRange.FromBounds(
                        lines.GetLineEnd(separatorLine),
                        lines.GetLineStart(separatorLine + 1)))
                    : lineEnding);
            }

            outputStarts[index] = range.Start + builder.Length;
            builder.Append(snapshot.GetText(lines.GetLineRange(order[index])));
        }

        var replacement = builder.ToString();
        var afterRange = lastLine < lines.LineCount - 1
            ? lines.GetLineStart(lastLine + 1) - range.End
            : 0;

        int MapPosition(int position, LineBlock block)
        {
            var location = lines.GetLineColumn(position);
            if (location.Line > block.Last)
            {
                var following = outputLine(block.Last) + 1;
                return following < order.Count
                    ? outputStarts[following]
                    : range.Start + replacement.Length + afterRange;
            }

            if (order.Count == 0)
            {
                return range.Start;
            }

            var output = outputLine(location.Line);
            var length = lines.GetLineLength(order[output]);
            return outputStarts[output] + Math.Min(location.Column, length);
        }

        var states = new List<TextCaretState>(carets.Count);
        var primaryIndex = 0;
        for (var index = 0; index < carets.Count; index++)
        {
            var caret = carets[index];
            var block = GetLineBlock(snapshot, caret.Selection);
            var selection = collapse
                ? TextSelection.Caret(MapPosition(caret.Selection.Start, block))
                : new TextSelection(
                    MapPosition(caret.Selection.Anchor, block),
                    MapPosition(caret.Selection.Active, block));
            var existing = states.FindIndex(state => state.Selection == selection);
            if (existing < 0)
            {
                existing = states.Count;
                states.Add(caret.WithSelection(selection));
            }

            if (index == carets.PrimaryIndex)
            {
                primaryIndex = existing;
            }
        }

        return new TextCaretEdit(range, replacement, new TextCaretSet(states, primaryIndex));
    }

    private readonly record struct LineBlock(int First, int Last);
}
