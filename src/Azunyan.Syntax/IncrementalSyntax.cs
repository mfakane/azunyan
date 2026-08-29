using Azunyan.Core;

namespace Azunyan.Syntax;

internal static class IncrementalSyntax
{
    public static SyntaxAnalysis Merge(
        SyntaxAnalysis previous,
        TextChange change,
        TextRange previousWindow,
        TextRange currentWindow,
        IReadOnlyList<SyntaxSpan> currentSpans,
        IReadOnlyList<SyntaxSpan>? currentCandidates = null,
        SyntaxProviderState? state = null)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(currentSpans);

        var delta = change.NewText.Length - change.OldRange.Length;
        var spans = MergeRanges(
            previous.Spans,
            currentSpans,
            previousWindow,
            delta,
            currentWindow);
        var candidates = MergeRanges(
            previous.Candidates,
            currentCandidates ?? currentSpans,
            previousWindow,
            delta,
            currentWindow);
        return new SyntaxAnalysis(spans, candidates, state);
    }

    public static TextRange Expand(
        TextRange range,
        int before,
        int after,
        int length)
    {
        var start = Math.Max(0, range.Start - before);
        var end = Math.Min(length, checked(range.End + after));
        if (end == start && length > 0)
        {
            end = Math.Min(length, start + 1);
        }

        return TextRange.FromBounds(start, end);
    }

    public static TextRange MapAfterChange(
        TextRange previousRange,
        TextChange change,
        int currentLength)
    {
        var delta = change.NewText.Length - change.OldRange.Length;
        var start = MapPosition(previousRange.Start, change, delta);
        var end = MapPosition(previousRange.End, change, delta);
        return TextRange.FromBounds(
            Math.Clamp(start, 0, currentLength),
            Math.Clamp(end, 0, currentLength));
    }

    public static TextRange ExpandToLine(
        TextSnapshot snapshot,
        TextRange range,
        bool includeNextLine = true)
    {
        var firstLine = snapshot.Lines.GetLine(range.Start);
        var lastPosition = range.Length == 0
            ? range.Start
            : Math.Min(snapshot.Length, range.End - 1);
        var lastLine = snapshot.Lines.GetLine(lastPosition);
        if (includeNextLine && lastLine + 1 < snapshot.Lines.LineCount)
        {
            lastLine++;
        }

        return TextRange.FromBounds(
            snapshot.Lines.GetLineStart(firstLine),
            snapshot.Lines.GetLineEnd(lastLine));
    }

    public static TextRange ExpandToIdentifier(
        TextSnapshot snapshot,
        TextRange range,
        Func<char, bool> isIdentifierPart)
    {
        var start = Math.Min(range.Start, snapshot.Length);
        var end = Math.Min(range.End, snapshot.Length);
        while (start > 0 && isIdentifierPart(snapshot[start - 1]))
        {
            start--;
        }

        while (end < snapshot.Length && isIdentifierPart(snapshot[end]))
        {
            end++;
        }

        if (start == end && snapshot.Length > 0)
        {
            end = Math.Min(snapshot.Length, start + 1);
        }

        return TextRange.FromBounds(start, end);
    }

    public static TextRange ExpandToPreviousSpan(
        IReadOnlyList<SyntaxSpan> spans,
        TextRange changeRange,
        TextRange fallback,
        int snapshotLength)
    {
        var start = fallback.Start;
        var end = fallback.End;
        foreach (var span in spans)
        {
            if (!Intersects(span.Range, changeRange))
            {
                continue;
            }

            start = Math.Min(start, span.Range.Start);
            end = Math.Max(end, span.Range.End);
        }

        return TextRange.FromBounds(
            Math.Clamp(start, 0, snapshotLength),
            Math.Clamp(end, 0, snapshotLength));
    }

    private static SyntaxSpan[] MergeRanges(
        IReadOnlyList<SyntaxSpan> previous,
        IReadOnlyList<SyntaxSpan> current,
        TextRange previousWindow,
        int delta,
        TextRange currentWindow)
    {
        var result = new List<SyntaxSpan>(previous.Count + current.Count);
        foreach (var span in previous)
        {
            if (span.Range.End <= previousWindow.Start)
            {
                result.Add(span);
            }
            else if (span.Range.Start >= previousWindow.End)
            {
                result.Add(Rebase(span, delta));
            }
        }

        result.AddRange(current.Where(span => currentWindow.Contains(span.Range)));
        return result
            .OrderBy(span => span.Range.Start)
            .ThenBy(span => span.Range.End)
            .ThenBy(span => span.Classification, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool Intersects(TextRange left, TextRange right)
    {
        if (right.IsEmpty)
        {
            return left.Start <= right.Start && right.Start <= left.End;
        }

        return left.Start < right.End && right.Start < left.End;
    }

    private static int MapPosition(int position, TextChange change, int delta) =>
        position <= change.OldRange.Start
            ? position
            : position >= change.OldRange.End
                ? checked(position + delta)
                : change.NewRange.Start;

    private static SyntaxSpan Rebase(SyntaxSpan span, int delta) =>
        new(
            TextRange.FromBounds(
                checked(span.Range.Start + delta),
                checked(span.Range.End + delta)),
            span.Classification);
}
