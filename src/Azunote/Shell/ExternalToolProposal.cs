using Azunyan.Core;

namespace Azunote;

/// <summary>
/// A change an external tool proposes to the text it read, shown as a diff
/// until it is applied or discarded.
/// </summary>
public sealed class ExternalToolProposal
{
    private readonly Func<string?> _apply;
    private readonly Action _release;

    internal ExternalToolProposal(string title, string diff, Func<string?> apply, Action release)
    {
        Title = title;
        Diff = diff;
        _apply = apply;
        _release = release;
    }

    /// <summary>Names the tool and the document, for the window showing the diff.</summary>
    public string Title { get; }

    public string Diff { get; }

    /// <summary>
    /// Applies the change, returning null, or the reason it cannot be applied
    /// while leaving the document as it is.
    /// </summary>
    public string? Apply() => _apply();

    /// <summary>Stops following the document; the proposal can no longer be applied.</summary>
    public void Discard() => _release();
}

/// <summary>
/// Follows the range a tool read through later edits: an edit before it moves
/// it, and an edit after it leaves it be. An edit inside the range, or any edit
/// when the whole document was read, means the text the tool read is gone.
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

    /// <summary>Whether an edit changed the text in the range.</summary>
    public bool IsChanged { get; private set; }

    public void Dispose() => _editor.Edited -= Editor_Edited;

    private void Editor_Edited(object? sender, TextChange change)
    {
        if (IsChanged)
        {
            return;
        }

        if (_wholeDocument
            || (change.OldRange.End > Range.Start && change.OldRange.Start < Range.End)
            || (change.OldRange.IsEmpty && change.OldRange.Start > Range.Start && change.OldRange.Start < Range.End))
        {
            IsChanged = true;
        }
        else if (change.OldRange.End <= Range.Start)
        {
            Range = new TextRange(Range.Start + change.NewText.Length - change.OldRange.Length, Range.Length);
        }
    }
}
