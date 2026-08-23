namespace Azunyan.Core;

/// <summary>
/// UI-independent mutable editing model. The current content is exposed as an
/// immutable snapshot; mutations are represented by one replacement change and
/// can therefore cross a UI or ABI boundary without fine-grained calls.
/// </summary>
public sealed class Document
{
    private readonly int _undoLimit;
    private readonly List<EditRecord> _undo = new();
    private readonly List<EditRecord> _redo = new();
    private TextTree _tree;
    private TextSnapshot _snapshot;
    private TextSelection _selection;

    public Document(string text = "", int undoLimit = 1000)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (undoLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(undoLimit));
        }

        _undoLimit = undoLimit;
        _tree = new TextTree(text);
        _snapshot = new TextSnapshot(_tree);
        _selection = TextSelection.Caret(0);
    }

    public TextSnapshot Snapshot => _snapshot;

    public TextSnapshot CurrentSnapshot => _snapshot;

    public string Text => _snapshot.Text;

    public int Length => _snapshot.Length;

    public TextSelection Selection
    {
        get => _selection;
        set
        {
            ValidateSelection(value);
            if (_selection == value)
            {
                return;
            }

            _selection = value;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public TextCaret Caret => new(_selection.CaretPosition);

    public int CaretPosition
    {
        get => _selection.CaretPosition;
        set => SetCaret(value);
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public event EventHandler<DocumentChangedEventArgs>? Changed;

    public event EventHandler? SelectionChanged;

    public void SetSelection(TextSelection selection)
    {
        Selection = selection;
    }

    public void SetSelection(int anchor, int active)
    {
        SetSelection(new TextSelection(anchor, active));
    }

    public void Select(TextRange range)
    {
        ValidateRange(range);
        SetSelection(new TextSelection(range.Start, range.End));
    }

    public void MoveCaret(int position, bool extendSelection = false)
    {
        ValidatePosition(position);
        SetSelection(extendSelection
            ? new TextSelection(_selection.Anchor, position)
            : TextSelection.Caret(position));
    }

    public void MoveCaretByGrapheme(int count, bool extendSelection = false) =>
        TextEditorCommands.MoveCaretByGrapheme(this, count, extendSelection);

    public void MoveCaretByScalar(int count, bool extendSelection = false) =>
        TextEditorCommands.MoveCaretByScalar(this, count, extendSelection);

    public TextChange DeleteBackward() => TextEditorCommands.DeleteBackward(this);

    public TextChange DeleteForward() => TextEditorCommands.DeleteForward(this);

    public TextChange DeleteBackwardByScalar() => TextEditorCommands.DeleteBackwardByScalar(this);

    public TextChange DeleteForwardByScalar() => TextEditorCommands.DeleteForwardByScalar(this);

    public void CollapseSelectionToStart() => SetCaret(_selection.Start);

    public void CollapseSelectionToEnd() => SetCaret(_selection.End);

    public void SetCaret(int position)
    {
        ValidatePosition(position);
        if (_selection == TextSelection.Caret(position))
        {
            return;
        }

        _selection = TextSelection.Caret(position);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public TextChange Insert(int position, string text)
    {
        ValidatePosition(position);
        ArgumentNullException.ThrowIfNull(text);
        return ApplyEdit(TextRange.Empty(position), text);
    }

    public TextChange Insert(string text) => ApplyEdit(_selection.Range, text);

    public TextChange Delete(TextRange range) => ApplyEdit(range, string.Empty);

    public TextChange Delete(int start, int length) => Delete(new TextRange(start, length));

    public TextChange DeleteSelection() => Delete(_selection.Range);

    public TextChange Replace(TextRange range, string text) => ApplyEdit(range, text);

    public TextChange Replace(int start, int length, string text) => Replace(new TextRange(start, length), text);

    public TextChange Replace(string text) => ApplyEdit(_selection.Range, text);

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        var record = RemoveLast(_undo);
        _redo.Add(record);

        var oldSnapshot = _snapshot;
        var oldSelection = _selection;
        _tree = record.OldTree;
        _snapshot = new TextSnapshot(_tree);
        _selection = record.OldSelection;

        var inverse = record.Change.Inverse();
        Changed?.Invoke(
            this,
            new DocumentChangedEventArgs(
                oldSnapshot,
                _snapshot,
                inverse,
                oldSelection,
                _selection,
                DocumentChangeKind.Undo));
        if (oldSelection != _selection)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        var record = RemoveLast(_redo);
        _undo.Add(record);

        var oldSnapshot = _snapshot;
        var oldSelection = _selection;
        _tree = record.NewTree;
        _snapshot = new TextSnapshot(_tree);
        _selection = record.NewSelection;

        Changed?.Invoke(
            this,
            new DocumentChangedEventArgs(
                oldSnapshot,
                _snapshot,
                record.Change,
                oldSelection,
                _selection,
                DocumentChangeKind.Redo));
        if (oldSelection != _selection)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    public void ClearHistory()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private TextChange ApplyEdit(TextRange range, string newText)
    {
        ValidateRange(range);
        ArgumentNullException.ThrowIfNull(newText);

        var oldText = _snapshot.GetText(range);
        var change = new TextChange(range, oldText, newText);
        var oldSnapshot = _snapshot;
        var oldSelection = _selection;
        var newSelection = TextSelection.Caret(checked(range.Start + newText.Length));

        if (string.Equals(oldText, newText, StringComparison.Ordinal))
        {
            _selection = newSelection;
            if (oldSelection != _selection)
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }

            return change;
        }

        var oldTree = _tree;
        var newTree = _tree.Replace(range, newText);
        _tree = newTree;
        _snapshot = new TextSnapshot(_tree);
        _selection = newSelection;

        var record = new EditRecord(change, oldTree, newTree, oldSelection, newSelection);
        AddUndo(record);
        _redo.Clear();

        Changed?.Invoke(
            this,
            new DocumentChangedEventArgs(
                oldSnapshot,
                _snapshot,
                change,
                oldSelection,
                newSelection,
                DocumentChangeKind.Edit));
        if (oldSelection != newSelection)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        return change;
    }

    private void AddUndo(EditRecord record)
    {
        if (_undoLimit == 0)
        {
            return;
        }

        _undo.Add(record);
        if (_undo.Count > _undoLimit)
        {
            _undo.RemoveAt(0);
        }
    }

    private void ValidatePosition(int position)
    {
        if (position < 0 || position > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }
    }

    private void ValidateRange(TextRange range)
    {
        if (range.Start > Length || range.End > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(range));
        }
    }

    private void ValidateSelection(TextSelection selection)
    {
        if (selection.Start > Length || selection.End > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(selection));
        }
    }

    private static EditRecord RemoveLast(List<EditRecord> records)
    {
        var lastIndex = records.Count - 1;
        var record = records[lastIndex];
        records.RemoveAt(lastIndex);
        return record;
    }

    private sealed record EditRecord(
        TextChange Change,
        TextTree OldTree,
        TextTree NewTree,
        TextSelection OldSelection,
        TextSelection NewSelection);
}
