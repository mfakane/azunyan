namespace Azunote;

/// <summary>
/// Carries the changes from a base text to a proposed one over to a copy of
/// the base that was edited meanwhile, line by line. A proposed change whose
/// lines overlap an edit is left out as a conflict; changes on neighbouring
/// lines both stand.
/// </summary>
public static class ThreeWayMerge
{
    /// <summary>
    /// A proposed change that was left out, by the zero-based lines of the
    /// edited text it overlaps, end exclusive.
    /// </summary>
    public readonly record struct Conflict(int StartLine, int EndLine);

    public sealed record Result(string Text, IReadOnlyList<Conflict> Conflicts);

    private readonly record struct Change(int BaseStart, int BaseEnd, int NewStart, int NewEnd)
    {
        public bool Overlaps(Change other) =>
            (BaseStart == BaseEnd, other.BaseStart == other.BaseEnd) switch
            {
                (false, false) => BaseStart < other.BaseEnd && other.BaseStart < BaseEnd,
                (true, true) => BaseStart == other.BaseStart,
                (true, false) => other.BaseStart < BaseStart && BaseStart < other.BaseEnd,
                (false, true) => BaseStart < other.BaseStart && other.BaseStart < BaseEnd
            };
    }

    public static Result Merge(string baseText, string editedText, string proposedText)
    {
        ArgumentNullException.ThrowIfNull(baseText);
        ArgumentNullException.ThrowIfNull(editedText);
        ArgumentNullException.ThrowIfNull(proposedText);
        var baseLines = UnifiedDiff.SplitLines(baseText);
        var editedLines = UnifiedDiff.SplitLines(editedText);
        var proposedLines = UnifiedDiff.SplitLines(proposedText);
        var editedScript = UnifiedDiff.Compare(baseLines, editedLines);
        var edits = GetChanges(editedScript);
        var positions = GetPositions(editedScript, baseLines.Length, editedLines.Length);

        var result = editedLines.ToList();
        var conflicts = new List<Conflict>();
        foreach (var change in GetChanges(UnifiedDiff.Compare(baseLines, proposedLines)).AsEnumerable().Reverse())
        {
            var overlapping = edits.Where(change.Overlaps).ToArray();
            if (overlapping.Length > 0)
            {
                var start = overlapping.Min(edit => edit.NewStart);
                var end = overlapping.Max(edit => edit.NewEnd);
                // An edit that only removed lines still marks where they were.
                end = Math.Max(end, Math.Min(start + 1, editedLines.Length));
                conflicts.Add(new Conflict(Math.Min(start, Math.Max(0, editedLines.Length - 1)), end));
                continue;
            }

            var at = positions[change.BaseStart];
            result.RemoveRange(at, change.BaseEnd - change.BaseStart);
            result.InsertRange(at, proposedLines[change.NewStart..change.NewEnd]);
        }

        // The result keeps the line endings of the edited text, which a tool
        // may not print.
        var newline = (editedText.Contains('\n') ? editedText : proposedText).Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";
        // The final line break follows the proposal when it changed one, and
        // the edited text otherwise.
        var endsWithNewline = baseText.EndsWith('\n') == proposedText.EndsWith('\n')
            ? editedText.EndsWith('\n')
            : proposedText.EndsWith('\n');
        var text = string.Join(newline, result) + (endsWithNewline && result.Count > 0 ? newline : string.Empty);
        conflicts.Reverse();
        return new Result(text, conflicts);
    }

    private static List<Change> GetChanges(List<UnifiedDiff.Edit> script)
    {
        var changes = new List<Change>();
        for (var index = 0; index < script.Count;)
        {
            if (script[index].Kind == ' ')
            {
                index++;
                continue;
            }

            var first = script[index];
            var removed = 0;
            var added = 0;
            for (; index < script.Count && script[index].Kind != ' '; index++)
            {
                if (script[index].Kind == '-')
                {
                    removed++;
                }
                else
                {
                    added++;
                }
            }

            changes.Add(new Change(first.OldLine, first.OldLine + removed, first.NewLine, first.NewLine + added));
        }

        return changes;
    }

    // Where each base line boundary falls in the edited text, for the
    // boundaries the edits leave in place.
    private static int[] GetPositions(List<UnifiedDiff.Edit> script, int baseCount, int editedCount)
    {
        var positions = new int[baseCount + 1];
        foreach (var edit in script)
        {
            if (edit.Kind != '+' && edit.OldLine < baseCount)
            {
                positions[edit.OldLine] = edit.NewLine;
            }
        }

        positions[baseCount] = editedCount;
        return positions;
    }
}
