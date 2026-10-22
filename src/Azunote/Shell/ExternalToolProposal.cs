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

    /// <summary>Raised when an edit to the document changed the diff.</summary>
    public event EventHandler? Changed;

    /// <summary>Names the tool and the document, for the window showing the diff.</summary>
    public string Title => $"{_source} proposes a change to {_name}.";

    public string Diff { get; private set; } = string.Empty;

    /// <summary>How many proposed changes are left out for overlapping an edit.</summary>
    public int ConflictCount { get; private set; }

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
        if (IsEmpty)
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

    private void Range_Changed(object? sender, EventArgs args)
    {
        Refresh();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Refresh()
    {
        var text = _editor.Text;
        var range = _range.Range;
        var edited = text.Substring(range.Start, Math.Min(range.Length, text.Length - range.Start));
        var merge = ThreeWayMerge.Merge(_baseText, edited, _proposedText);
        _result = merge.Text;
        var firstLine = text.AsSpan(0, range.Start).Count('\n') + 1;
        Diff = UnifiedDiff.Create(edited, merge.Text, "a/" + _name, "b/" + _name, firstLine);
        ConflictCount = merge.Conflicts.Count;
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
