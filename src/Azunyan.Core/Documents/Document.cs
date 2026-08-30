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
    private TextCaretSet _caretSet;

    public Document(string text = "", int undoLimit = 1000)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(undoLimit);

        _undoLimit = undoLimit;
        _tree = new TextTree(text);
        _snapshot = new TextSnapshot(_tree);
        _caretSet = new TextCaretSet(new[] {
            new TextCaretState(TextSelection.Caret(0), 0)
        });
    }

    public TextSnapshot Snapshot => _snapshot;

    public TextSnapshot CurrentSnapshot => _snapshot;

    public string Text => _snapshot.Text;

    public int Length => _snapshot.Length;

    /// <summary>
    /// The primary selection. Assigning it intentionally collapses a
    /// multi-caret document back to one caret, preserving the old API contract.
    /// </summary>
    public TextSelection Selection
    {
        get => _caretSet.Primary.Selection;
        set
        {
            ValidateSelection(value);
            SetCaretSetCore(new TextCaretSet(new[] {
                new TextCaretState(value, GetDisplayColumn(value.CaretPosition))
            }));
        }
    }

    public TextCaretSet CaretSet => _caretSet;

    public TextCaret Caret => new(Selection.CaretPosition);

    public int CaretPosition
    {
        get => Selection.CaretPosition;
        set => SetCaret(value);
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public event EventHandler<DocumentChangedEventArgs>? Changed;

    public event EventHandler? SelectionChanged;

    public event EventHandler? CaretSetChanged;

    public void SetCaretSet(TextCaretSet caretSet)
    {
        ArgumentNullException.ThrowIfNull(caretSet);
        foreach (var caret in caretSet)
        {
            ValidateSelection(caret.Selection);
        }

        SetCaretSetCore(caretSet);
    }

    public void SetSelection(TextSelection selection) => Selection = selection;

    public void SetSelection(int anchor, int active) =>
        SetSelection(new TextSelection(anchor, active));

    public void Select(TextRange range)
    {
        ValidateRange(range);
        SetSelection(new TextSelection(range.Start, range.End));
    }

    public void MoveCaret(int position, bool extendSelection = false)
    {
        ValidatePosition(position);
        SetSelection(extendSelection
            ? new TextSelection(Selection.Anchor, position)
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

    public void CollapseSelectionToStart() => SetCaret(Selection.Start);

    public void CollapseSelectionToEnd() => SetCaret(Selection.End);

    public void SetCaret(int position)
    {
        ValidatePosition(position);
        Selection = TextSelection.Caret(position);
    }

    public TextChange Insert(int position, string text)
    {
        ValidatePosition(position);
        ArgumentNullException.ThrowIfNull(text);
        return ApplyEdit(TextRange.Empty(position), text);
    }

    public TextChange Insert(string text) => ApplyEdit(Selection.Range, text);

    public TextChange Delete(TextRange range) => ApplyEdit(range, string.Empty);

    public TextChange Delete(int start, int length) => Delete(new TextRange(start, length));

    public TextChange DeleteSelection() => Delete(Selection.Range);

    public TextChange Replace(TextRange range, string text) => ApplyEdit(range, text);

    public TextChange Replace(
        TextRange range,
        string text,
        TextSelection selection) => ApplyEdit(range, text, requestedSelection: selection);

    public TextChange Replace(
        TextRange range,
        string text,
        TextCaretSet caretSet) => ApplyEdit(range, text, caretSet: caretSet);

    public TextChange Replace(int start, int length, string text) => Replace(new TextRange(start, length), text);

    public TextChange Replace(string text) => ApplyEdit(Selection.Range, text);

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        var record = RemoveLast(_undo);
        _redo.Add(record);

        var oldSnapshot = _snapshot;
        var oldSelection = Selection;
        var oldCaretSet = _caretSet;
        _tree = record.OldTree;
        _snapshot = new TextSnapshot(_tree);
        _caretSet = record.OldCaretSet;

        Changed?.Invoke(
            this,
            new DocumentChangedEventArgs(
                oldSnapshot,
                _snapshot,
                record.Change.Inverse(),
                oldSelection,
                Selection,
                DocumentChangeKind.Undo));
        RaiseCaretEvents(oldCaretSet, _caretSet, oldSelection);
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
        var oldSelection = Selection;
        var oldCaretSet = _caretSet;
        _tree = record.NewTree;
        _snapshot = new TextSnapshot(_tree);
        _caretSet = record.NewCaretSet;

        Changed?.Invoke(
            this,
            new DocumentChangedEventArgs(
                oldSnapshot,
                _snapshot,
                record.Change,
                oldSelection,
                Selection,
                DocumentChangeKind.Redo));
        RaiseCaretEvents(oldCaretSet, _caretSet, oldSelection);
        return true;
    }

    public void ClearHistory()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private TextChange ApplyEdit(
        TextRange range,
        string newText,
        TextSelection? requestedSelection = null,
        TextCaretSet? caretSet = null)
    {
        ValidateRange(range);
        ArgumentNullException.ThrowIfNull(newText);
        if (requestedSelection is not null && caretSet is not null)
        {
            throw new ArgumentException("A selection and a caret set cannot both be supplied.");
        }

        var oldText = _snapshot.GetText(range);
        var change = new TextChange(range, oldText, newText);
        var oldSnapshot = _snapshot;
        var oldSelection = Selection;
        var oldCaretSet = _caretSet;
        var newSelection = requestedSelection
            ?? TextSelection.Caret(checked(range.Start + newText.Length));
        var newLength = checked(Length - range.Length + newText.Length);
        ValidateSelection(newSelection, newLength);
        var newCaretSet = caretSet ?? new TextCaretSet(new[] {
            new TextCaretState(newSelection, GetDisplayColumn(newSelection.CaretPosition, newLength))
        });
        foreach (var caret in newCaretSet)
        {
            ValidateSelection(caret.Selection, newLength);
        }

        if (string.Equals(oldText, newText, StringComparison.Ordinal))
        {
            SetCaretSetCore(newCaretSet);
            return change;
        }

        var oldTree = _tree;
        var newTree = _tree.Replace(range, newText);
        _tree = newTree;
        _snapshot = new TextSnapshot(_tree);
        _caretSet = newCaretSet;

        var record = new EditRecord(change, oldTree, newTree, oldCaretSet, newCaretSet);
        AddUndo(record);
        _redo.Clear();

        Changed?.Invoke(
            this,
            new DocumentChangedEventArgs(
                oldSnapshot,
                _snapshot,
                change,
                oldSelection,
                Selection,
                DocumentChangeKind.Edit));
        RaiseCaretEvents(oldCaretSet, newCaretSet, oldSelection);
        return change;
    }

    private void SetCaretSetCore(TextCaretSet caretSet)
    {
        var old = _caretSet;
        if (old.Equals(caretSet))
        {
            return;
        }

        _caretSet = caretSet;
        RaiseCaretEvents(old, caretSet, old.Primary.Selection);
    }

    private void RaiseCaretEvents(
        TextCaretSet oldCaretSet,
        TextCaretSet newCaretSet,
        TextSelection oldPrimarySelection)
    {
        if (!oldCaretSet.Equals(newCaretSet))
        {
            CaretSetChanged?.Invoke(this, EventArgs.Empty);
        }

        if (oldPrimarySelection != newCaretSet.Primary.Selection)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
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

    private void ValidateSelection(TextSelection selection) =>
        ValidateSelection(selection, Length);

    private static void ValidateSelection(TextSelection selection, int length)
    {
        if (selection.Start > length || selection.End > length)
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

    private int GetDisplayColumn(int position, int? length = null)
    {
        var bounded = Math.Clamp(position, 0, Math.Min(length ?? Length, Length));
        var line = _snapshot.Lines.GetLine(bounded);
        var lineStart = _snapshot.Lines.GetLineStart(line);
        var lineEnd = _snapshot.Lines.GetLineEnd(line);
        var local = Math.Clamp(bounded - lineStart, 0, lineEnd - lineStart);
        var column = 0;
        for (var index = 0; index < local; index++)
        {
            column = _snapshot.Text[lineStart + index] == '\t'
                ? column + 4 - (column % 4)
                : column + 1;
        }

        return column;
    }

    private sealed record EditRecord(
        TextChange Change,
        TextTree OldTree,
        TextTree NewTree,
        TextCaretSet OldCaretSet,
        TextCaretSet NewCaretSet)
    {
        public TextSelection OldSelection => OldCaretSet.Primary.Selection;

        public TextSelection NewSelection => NewCaretSet.Primary.Selection;
    }
}
