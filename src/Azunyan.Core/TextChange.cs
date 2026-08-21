namespace Azunyan.Core;

/// <summary>
/// A single coarse-grained replacement. <see cref="OldRange"/> describes the
/// range in the document before the change; <see cref="NewRange"/> describes
/// the inserted text in the document after the change.
/// </summary>
public readonly record struct TextChange
{
    public TextChange(TextRange oldRange, string oldText, string newText)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);
        OldRange = oldRange;
        OldText = oldText;
        NewText = newText;
    }

    public TextRange OldRange { get; }

    public string OldText { get; }

    public string NewText { get; }

    public TextRange NewRange => new(OldRange.Start, NewText.Length);

    public bool IsInsertion => OldRange.IsEmpty && NewText.Length > 0;

    public bool IsDeletion => OldRange.Length > 0 && NewText.Length == 0;

    public bool IsReplacement => OldRange.Length > 0 && NewText.Length > 0;

    public TextChange Inverse() => new(NewRange, NewText, OldText);
}

public enum DocumentChangeKind
{
    Edit,
    Undo,
    Redo
}

public sealed class DocumentChangedEventArgs : EventArgs
{
    public DocumentChangedEventArgs(
        TextSnapshot oldSnapshot,
        TextSnapshot newSnapshot,
        TextChange change,
        TextSelection oldSelection,
        TextSelection newSelection,
        DocumentChangeKind kind)
    {
        OldSnapshot = oldSnapshot;
        NewSnapshot = newSnapshot;
        Change = change;
        OldSelection = oldSelection;
        NewSelection = newSelection;
        Kind = kind;
    }

    public TextSnapshot OldSnapshot { get; }

    public TextSnapshot NewSnapshot { get; }

    public TextChange Change { get; }

    public TextSelection OldSelection { get; }

    public TextSelection NewSelection { get; }

    public DocumentChangeKind Kind { get; }
}
