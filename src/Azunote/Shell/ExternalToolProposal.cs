using Azunyan.Core;

namespace Azunote;

/// <summary>
/// A change an external tool proposes to the text it read, shown as a diff
/// until it is applied or discarded. The document stays editable meanwhile:
/// each edit carries the proposed changes over to the edited text, leaving out
/// those that overlap an edit, and the diff is made again from it. The
/// changes left out are marked in the document as warnings.
/// </summary>
public sealed class ExternalToolProposal
{
    private readonly IEditorBuffer _editor;
    private readonly TrackedTextRange _range;
    private readonly string _baseText;
    private readonly string _proposedText;
    private readonly string _source;
    private readonly string _name;
    private readonly DocumentDiagnostics? _diagnostics;
    private readonly Action _observeText;
    private readonly string _key = "proposal:" + Guid.NewGuid().ToString("N");
    private string _result = string.Empty;
    private bool _released;
    private bool _stale;
    private string _diff = string.Empty;
    private int _conflictCount;

    // What the diff's lines stand for, to map positions between it and the
    // document: the diff lines, where each starts, the text they were made
    // from and where its lines start, and where that text is in the document.
    private List<UnifiedDiff.DiffLine> _lines = [];
    private int[] _diffLineStarts = [];
    private Dictionary<int, int> _diffLineOfOldLine = [];
    private string _edited = string.Empty;
    private int[] _editedLineStarts = [0];
    private int _rangeStart;

    internal ExternalToolProposal(
        IEditorBuffer editor,
        TrackedTextRange range,
        string baseText,
        string proposedText,
        string source,
        string name,
        DocumentDiagnostics? diagnostics,
        Action observeText)
    {
        _editor = editor;
        _range = range;
        _baseText = baseText;
        _proposedText = proposedText;
        _source = source;
        _name = name;
        _diagnostics = diagnostics;
        _observeText = observeText;
        _range.Changed += Range_Changed;
        Refresh();
    }

    /// <summary>
    /// Raised when an edit to the document changed the diff. The diff is made
    /// again when it is next read, which is meant to be after the editor has
    /// finished with the edit: making it updates the document's warnings,
    /// which the editor must not be asked to show while it applies an edit.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Names the tool and the document, for the window showing the diff.</summary>
    public string Title => $"{_source} proposes a change to {_name}.";

    public string Diff
    {
        get
        {
            EnsureCurrent();
            return _diff;
        }
    }

    /// <summary>How many proposed changes are left out for overlapping an edit.</summary>
    public int ConflictCount
    {
        get
        {
            EnsureCurrent();
            return _conflictCount;
        }
    }

    /// <summary>Whether no change is left to apply.</summary>
    public bool IsEmpty => Diff.Length == 0;

    /// <summary>
    /// Applies the changes that are left, returning null, or the reason
    /// nothing could be applied.
    /// </summary>
    public string? Apply()
    {
        if (_released)
        {
            return "The proposal is no longer open.";
        }

        Refresh();
        if (_diff.Length == 0)
        {
            return "No change is left to apply.";
        }

        var range = _range.Range;
        var result = _result;
        Release();
        _editor.Replace(range, result);
        _observeText();
        return null;
    }

    /// <summary>Stops following the document; the proposal can no longer be applied.</summary>
    public void Discard() => Release();

    public TextSelection DocumentSelection => _editor.Selection;

    public void SelectInDocument(TextSelection selection)
    {
        _editor.SetSelection(selection);
        (_editor as IEditorView)?.ScrollSelectionIntoView();
    }

    /// <summary>
    /// The document selection a diff selection stands for. A kept or removed
    /// line is that line of the document; an added line or a header is where
    /// its change goes.
    /// </summary>
    public TextSelection ToDocument(TextSelection diffSelection)
    {
        EnsureCurrent();
        return new(ToDocument(diffSelection.Anchor), ToDocument(diffSelection.Active));
    }

    /// <summary>
    /// The diff selection a document selection stands for, or null when the
    /// diff shows none of it. A caret needs its own line shown; a selection
    /// takes the part of it the diff shows.
    /// </summary>
    public TextSelection? ToDiff(TextSelection documentSelection)
    {
        EnsureCurrent();
        if (documentSelection.IsEmpty)
        {
            return ToDiff(documentSelection.Active, nearest: null) is { } caret ? TextSelection.Caret(caret) : null;
        }

        if (ToDiff(documentSelection.Start, nearest: true) is not { } start
            || ToDiff(documentSelection.End, nearest: false) is not { } end
            || start > end)
        {
            return null;
        }

        return documentSelection.IsReversed ? new TextSelection(end, start) : new TextSelection(start, end);
    }

    private int ToDocument(int diffPosition)
    {
        if (_lines.Count == 0)
        {
            return _rangeStart;
        }

        var line = Math.Min(LineAt(_diffLineStarts, diffPosition), _lines.Count - 1);
        var entry = _lines[line];
        if (entry.Kind == '+')
        {
            // An added line that replaces removed ones stands for the first of
            // them, rather than for the line after them.
            for (var previous = line - 1; previous >= 0 && _lines[previous].Kind is '+' or '-'; previous--)
            {
                if (_lines[previous].Kind == '-')
                {
                    entry = entry with { OldLine = _lines[previous].OldLine };
                }
            }
        }

        var start = EditedLineStart(entry.OldLine);
        if (entry.Kind is ' ' or '-')
        {
            var column = Math.Max(0, diffPosition - _diffLineStarts[line] - 1);
            start = Math.Min(start + column, EditedLineEnd(entry.OldLine));
        }

        return _rangeStart + start;
    }

    // Exact when nearest is null; otherwise the first shown position after,
    // or the last one before, a line the diff leaves out.
    private int? ToDiff(int documentPosition, bool? nearest)
    {
        var offset = documentPosition - _rangeStart;
        if (offset < 0 || offset > _edited.Length)
        {
            return null;
        }

        var oldLine = LineAt(_editedLineStarts, offset);
        if (_diffLineOfOldLine.TryGetValue(oldLine, out var diffLine))
        {
            return _diffLineStarts[diffLine] + 1 + (offset - _editedLineStarts[oldLine]);
        }

        if (nearest is null)
        {
            return null;
        }

        var shown = nearest.Value
            ? _diffLineOfOldLine.Keys.Where(line => line > oldLine).DefaultIfEmpty(-1).Min()
            : _diffLineOfOldLine.Keys.Where(line => line < oldLine).DefaultIfEmpty(-1).Max();
        if (shown < 0)
        {
            return null;
        }

        var lineStart = _diffLineStarts[_diffLineOfOldLine[shown]] + 1;
        return nearest.Value ? lineStart : lineStart + (EditedLineEnd(shown) - EditedLineStart(shown));
    }

    private int EditedLineStart(int line) =>
        line < _editedLineStarts.Length ? _editedLineStarts[line] : _edited.Length;

    // The end of a line's text, before its line break.
    private int EditedLineEnd(int line)
    {
        var start = EditedLineStart(line);
        var end = line + 1 < _editedLineStarts.Length ? _editedLineStarts[line + 1] : _edited.Length;
        return start + _edited.AsSpan(start, end - start).TrimEnd("\r\n").Length;
    }

    private static int[] GetLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var index = text.IndexOf('\n'); index >= 0; index = text.IndexOf('\n', index + 1))
        {
            starts.Add(index + 1);
        }

        return [.. starts];
    }

    private static int LineAt(int[] starts, int position)
    {
        var index = Array.BinarySearch(starts, position);
        return index >= 0 ? index : ~index - 1;
    }

    private void Range_Changed(object? sender, EventArgs args)
    {
        _stale = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureCurrent()
    {
        if (_stale && !_released)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        _stale = false;
        var text = _editor.Text;
        var range = _range.Range;
        var edited = text.Substring(range.Start, Math.Min(range.Length, text.Length - range.Start));
        var merge = ThreeWayMerge.Merge(_baseText, edited, _proposedText);
        _result = merge.Text;
        var firstLine = text.AsSpan(0, range.Start).Count('\n') + 1;
        var lines = new List<UnifiedDiff.DiffLine>();
        _diff = UnifiedDiff.Create(edited, merge.Text, "a/" + _name, "b/" + _name, firstLine, lines);
        _lines = lines;
        _diffLineStarts = GetLineStarts(_diff);
        _diffLineOfOldLine = lines
            .Select((line, index) => (line, index))
            .Where(pair => pair.line.Kind is ' ' or '-')
            .ToDictionary(pair => pair.line.OldLine, pair => pair.index);
        _edited = edited;
        _editedLineStarts = GetLineStarts(edited);
        _rangeStart = range.Start;
        _conflictCount = merge.Conflicts.Count;
        _diagnostics?.Replace(
            _key,
            merge.Conflicts.Select(conflict => new DocumentDiagnostic(
                _key,
                _source,
                GetLines(edited, range.Start, conflict),
                DiagnosticSeverity.Warning,
                $"{_source} proposed a change here that overlaps an edit, so it is left out.")).ToArray());
    }

    // The document range of the conflict's lines, without their last line break.
    private static TextRange GetLines(string edited, int offset, ThreeWayMerge.Conflict conflict)
    {
        var start = 0;
        for (var line = 0; line < conflict.StartLine; line++)
        {
            start = edited.IndexOf('\n', start) + 1;
        }

        var end = start;
        for (var line = conflict.StartLine; line < conflict.EndLine; line++)
        {
            var next = edited.IndexOf('\n', end);
            end = next < 0 ? edited.Length : next + 1;
        }

        end = edited.AsSpan(start, end - start).TrimEnd("\r\n").Length + start;
        return TextRange.FromBounds(offset + start, offset + end);
    }

    private void Release()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        _range.Changed -= Range_Changed;
        _range.Dispose();
        _diagnostics?.Replace(_key, []);
    }
}

/// <summary>
/// Follows the text a tool read through later edits. An edit before it moves
/// it and an edit after it leaves it be; an edit inside it, or across one of
/// its ends, becomes part of it. The whole document stays the whole document.
/// </summary>
internal sealed class TrackedTextRange : IDisposable
{
    private readonly IEditorBuffer _editor;
    private readonly bool _wholeDocument;

    public TrackedTextRange(IEditorBuffer editor, TextRange range, bool wholeDocument)
    {
        _editor = editor;
        Range = range;
        _wholeDocument = wholeDocument;
        _editor.Edited += Editor_Edited;
    }

    public TextRange Range { get; private set; }

    /// <summary>Raised after an edit that moved the range or changed its text.</summary>
    public event EventHandler? Changed;

    public void Dispose() => _editor.Edited -= Editor_Edited;

    private void Editor_Edited(object? sender, TextChange change)
    {
        var delta = change.NewText.Length - change.OldRange.Length;
        if (_wholeDocument)
        {
            Range = new TextRange(0, Range.Length + delta);
        }
        else if (change.OldRange.End <= Range.Start)
        {
            Range = new TextRange(Range.Start + delta, Range.Length);
        }
        else if (change.OldRange.Start >= Range.End)
        {
            return;
        }
        else
        {
            Range = TextRange.FromBounds(
                Math.Min(Range.Start, change.OldRange.Start),
                Math.Max(Range.End, change.OldRange.End) + delta);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
