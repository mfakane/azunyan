namespace Azunyan.Core;

/// <summary>
/// The tab stops of a snippet just inserted into a document, followed through
/// the edits made in them. Each stop is selected in turn, every place with its
/// number at once, so typing in one changes them all; the final stop, $0 or
/// else the end of the snippet, ends it. The session ends too when an edit
/// changes anything but the selected stop.
/// </summary>
public sealed class SnippetSession
{
    private readonly TextRange[] _ranges;
    private readonly int[] _numbers;
    private readonly int[] _parents;
    private readonly bool[] _removed;
    private TextRange _bounds;
    private int _number;

    private SnippetSession(int offset, string text, IReadOnlyList<SnippetPlaceholder> placeholders)
    {
        var hasFinal = placeholders.Any(placeholder => placeholder.Number == 0);
        var count = placeholders.Count + (hasFinal ? 0 : 1);
        _ranges = new TextRange[count];
        _numbers = new int[count];
        _parents = new int[count];
        _removed = new bool[count];
        for (var index = 0; index < placeholders.Count; index++)
        {
            var placeholder = placeholders[index];
            _ranges[index] = new TextRange(offset + placeholder.Range.Start, placeholder.Range.Length);
            _numbers[index] = placeholder.Number;
            _parents[index] = placeholder.Parent;
        }

        if (!hasFinal)
        {
            _ranges[^1] = new TextRange(offset + text.Length, 0);
            _parents[^1] = -1;
        }

        _bounds = new TextRange(offset, text.Length);
        _number = Stops[0];
    }

    /// <summary>Whether the final stop is selected, after which the session has nothing left to do.</summary>
    public bool IsFinished => _number == 0;

    /// <summary>The numbers of the stops still in the snippet, in the order Tab visits them.</summary>
    private int[] Stops =>
    [
        .. _numbers
            .Where((number, index) => number > 0 && !_removed[index])
            .Distinct()
            .Order(),
        0
    ];

    /// <summary>
    /// Starts a session for <paramref name="text"/> inserted at
    /// <paramref name="offset"/>, with its first stop selected.
    /// </summary>
    public static SnippetSession Start(int offset, string text, IReadOnlyList<SnippetPlaceholder> placeholders)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(placeholders);
        return new SnippetSession(offset, text, placeholders);
    }

    /// <summary>A selection of every place of the selected stop, the first one primary.</summary>
    public TextCaretSet GetSelection(Func<int, int> displayColumn)
    {
        ArgumentNullException.ThrowIfNull(displayColumn);
        return new TextCaretSet(CurrentRanges().Select(index =>
        {
            var range = _ranges[index];
            return new TextCaretState(new TextSelection(range.Start, range.End), displayColumn(range.End));
        }));
    }

    /// <summary>Selects the next stop, or the previous one; the first stop stays when there is none before it.</summary>
    public void Move(int direction)
    {
        var stops = Stops;
        var current = Array.IndexOf(stops, _number);
        _number = stops[Math.Clamp(current + Math.Sign(direction), 0, stops.Length - 1)];
    }

    /// <summary>
    /// Whether every caret is in its own place of the selected stop, as the
    /// selection puts them, so that the session goes on.
    /// </summary>
    public bool Contains(TextCaretSet caretSet)
    {
        ArgumentNullException.ThrowIfNull(caretSet);
        var ranges = CurrentRanges().Select(index => _ranges[index]).ToArray();
        if (caretSet.Count != ranges.Length)
        {
            return false;
        }

        var selections = caretSet.Select(caret => caret.Selection).OrderBy(selection => selection.Start).ToArray();
        for (var index = 0; index < ranges.Length; index++)
        {
            if (selections[index].Start < ranges[index].Start || selections[index].End > ranges[index].End)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Follows an edit. An edit made the same way in every place of the
    /// selected stop moves the others along; an edit before the snippet moves
    /// it and one after it changes nothing. Any other edit ends the session,
    /// which this answers with false.
    /// </summary>
    public bool Apply(TextChange change, TextSnapshot oldSnapshot, TextSnapshot newSnapshot)
    {
        ArgumentNullException.ThrowIfNull(oldSnapshot);
        ArgumentNullException.ThrowIfNull(newSnapshot);
        var oldRange = change.OldRange;
        var delta = change.NewText.Length - change.OldText.Length;
        var mirrors = CurrentRanges();
        var first = _ranges[mirrors[0]];
        var last = _ranges[mirrors[^1]];
        if (oldRange.Start < first.Start || oldRange.End > last.End)
        {
            if (oldRange.End <= _bounds.Start)
            {
                Shift(delta);
                return true;
            }

            return oldRange.Start >= _bounds.End;
        }

        if (delta % mirrors.Length != 0)
        {
            return false;
        }

        var each = delta / mirrors.Length;
        var mapped = new TextRange[_ranges.Length];
        for (var index = 0; index < _ranges.Length; index++)
        {
            var range = _ranges[index];
            var mirror = Array.IndexOf(mirrors, index);
            if (mirror >= 0)
            {
                mapped[index] = TextRange.FromBounds(range.Start + (each * mirror), range.End + (each * (mirror + 1)));
                continue;
            }

            if (mirrors.Any(other => IsAncestor(other, index)))
            {
                // A placeholder inside the default text being typed over is gone.
                _removed[index] = true;
                mapped[index] = range;
                continue;
            }

            var before = mirrors.Count(other => other < index && !IsAncestor(index, other));
            var inside = mirrors.Count(other => IsAncestor(index, other));
            mapped[index] = TextRange.FromBounds(range.Start + (each * before), range.End + (each * (before + inside)));
        }

        // Every place has to hold the same text, and the text between them
        // has to stay, for the edit to be one made in each place.
        string? text = null;
        for (var mirror = 0; mirror < mirrors.Length; mirror++)
        {
            var range = mapped[mirrors[mirror]];
            if (range.Start < 0 || range.End > newSnapshot.Length || newSnapshot.GetText(range) != (text ??= newSnapshot.GetText(range)))
            {
                return false;
            }

            if (mirror > 0)
            {
                var oldBetween = TextRange.FromBounds(_ranges[mirrors[mirror - 1]].End, _ranges[mirrors[mirror]].Start);
                var newBetween = TextRange.FromBounds(mapped[mirrors[mirror - 1]].End, range.Start);
                if (oldBetween.Length != newBetween.Length || oldSnapshot.GetText(oldBetween) != newSnapshot.GetText(newBetween))
                {
                    return false;
                }
            }
        }

        mapped.CopyTo(_ranges, 0);
        _bounds = TextRange.FromBounds(_bounds.Start, _bounds.End + delta);
        return true;
    }

    /// <summary>The places of the selected stop, in order in the document.</summary>
    private int[] CurrentRanges()
    {
        var number = _number;
        return [.. Enumerable.Range(0, _ranges.Length)
            .Where(index => _numbers[index] == number && !_removed[index])
            .OrderBy(index => _ranges[index].Start)
            .ThenBy(index => index)];
    }

    private bool IsAncestor(int ancestor, int index)
    {
        for (var parent = _parents[index]; parent >= 0; parent = _parents[parent])
        {
            if (parent == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    private void Shift(int delta)
    {
        for (var index = 0; index < _ranges.Length; index++)
        {
            _ranges[index] = new TextRange(_ranges[index].Start + delta, _ranges[index].Length);
        }

        _bounds = new TextRange(_bounds.Start + delta, _bounds.Length);
    }
}
