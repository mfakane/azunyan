namespace Azunyan.Core;

/// <summary>
/// UI-independent mutable editing model. The current content is exposed as an
/// immutable snapshot; mutations are represented by one replacement change and
/// can therefore cross a UI or ABI boundary without fine-grained calls.
/// </summary>
public sealed class Document
{
    private readonly SharedBuffer _buffer;
    private TextCaretSet _caretSet;
    private bool _disposed;

    public Document(string text = "", int undoLimit = 1000)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(undoLimit);

        _buffer = new SharedBuffer(text, undoLimit);
        _caretSet = new TextCaretSet(new[] {
            new TextCaretState(TextSelection.Caret(0), 0)
        });
        _buffer.Changed += OnBufferChanged;
    }

    private Document(SharedBuffer buffer)
    {
        _buffer = buffer;
        _caretSet = new TextCaretSet(new[] {
            new TextCaretState(TextSelection.Caret(0), 0)
        });
        _buffer.Changed += OnBufferChanged;
    }

    /// <summary>
    /// Creates another editing view over the same text buffer and undo history.
    /// Selection and caret state remain local to each view.
    /// </summary>
    public Document CreateView()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new(_buffer);
    }

    /// <summary>Releases this view from its shared buffer.</summary>
    public void CloseView()
    {
        if (_disposed) return;
        _disposed = true;
        _buffer.Changed -= OnBufferChanged;
    }

    public TextSnapshot Snapshot => _buffer.Snapshot;

    public TextSnapshot CurrentSnapshot => _buffer.Snapshot;

    public string Text => Snapshot.Text;

    public int Length => Snapshot.Length;

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

    public bool CanUndo => _buffer.CanUndo;

    public bool CanRedo => _buffer.CanRedo;

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
        return _buffer.Undo(this);
    }

    public bool Redo()
    {
        return _buffer.Redo(this);
    }

    public void ClearHistory()
    {
        _buffer.ClearHistory();
    }

    /// <summary>Replaces the shared contents and clears its undo history.</summary>
    public void Reset(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var caret = new TextCaretSet(new[] {
            new TextCaretState(TextSelection.Caret(0), 0)
        });
        _buffer.Reset(text, this, caret);
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

        var oldText = Snapshot.GetText(range);
        var change = new TextChange(range, oldText, newText);
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

        _buffer.Apply(change, this, oldCaretSet, newCaretSet);
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

    private int GetDisplayColumn(int position, int? length = null)
    {
        var bounded = Math.Clamp(position, 0, Math.Min(length ?? Length, Length));
        var line = Snapshot.Lines.GetLine(bounded);
        var lineStart = Snapshot.Lines.GetLineStart(line);
        var lineEnd = Snapshot.Lines.GetLineEnd(line);
        var local = Math.Clamp(bounded - lineStart, 0, lineEnd - lineStart);
        var column = 0;
        for (var index = 0; index < local; index++)
        {
            column = Snapshot.Text[lineStart + index] == '\t'
                ? column + 4 - (column % 4)
                : column + 1;
        }

        return column;
    }

    private void OnBufferChanged(object? sender, BufferChangedEventArgs args)
    {
        var oldCaretSet = _caretSet;
        var oldSelection = Selection;
        _caretSet = ReferenceEquals(args.Initiator, this) && args.InitiatorCaretSet is not null
            ? args.InitiatorCaretSet
            : MapCaretSet(oldCaretSet, args.Change);

        Changed?.Invoke(this, new DocumentChangedEventArgs(
            args.OldSnapshot,
            args.NewSnapshot,
            args.Change,
            oldSelection,
            Selection,
            args.Kind));
        RaiseCaretEvents(oldCaretSet, _caretSet, oldSelection);
    }

    private TextCaretSet MapCaretSet(TextCaretSet caretSet, TextChange change) =>
        new(caretSet.Select(caret =>
        {
            var selection = new TextSelection(
                MapPosition(caret.Selection.Anchor, change),
                MapPosition(caret.Selection.Active, change));
            return new TextCaretState(
                selection,
                GetDisplayColumn(selection.CaretPosition));
        }));

    private static int MapPosition(int position, TextChange change)
    {
        if (position <= change.OldRange.Start)
        {
            return position;
        }

        if (position >= change.OldRange.End)
        {
            return checked(position + change.NewText.Length - change.OldText.Length);
        }

        return change.NewRange.End;
    }

    private sealed class SharedBuffer
    {
        private readonly int _undoLimit;
        private readonly List<EditRecord> _undo = [];
        private readonly List<EditRecord> _redo = [];
        private TextTree _tree;

        public SharedBuffer(string text, int undoLimit)
        {
            _undoLimit = undoLimit;
            _tree = new TextTree(text);
            Snapshot = new TextSnapshot(_tree);
        }

        public TextSnapshot Snapshot { get; private set; }
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public event EventHandler<BufferChangedEventArgs>? Changed;

        public void Apply(TextChange change, Document initiator, TextCaretSet oldCarets, TextCaretSet newCarets)
        {
            var oldTree = _tree;
            var newTree = _tree.Replace(change.OldRange, change.NewText);
            var record = new EditRecord(change, oldTree, newTree, initiator, oldCarets, newCarets);
            _tree = newTree;
            if (_undoLimit > 0)
            {
                _undo.Add(record);
                if (_undo.Count > _undoLimit)
                {
                    _undo.RemoveAt(0);
                }
            }
            _redo.Clear();
            Publish(initiator, newCarets, DocumentChangeKind.Edit, change);
        }

        public bool Undo(Document initiator)
        {
            if (_undo.Count == 0) return false;
            var record = RemoveLast(_undo);
            _redo.Add(record);
            _tree = record.OldTree;
            Publish(initiator,
                ReferenceEquals(record.Origin, initiator) ? record.OldCaretSet : null,
                DocumentChangeKind.Undo, record.Change.Inverse());
            return true;
        }

        public bool Redo(Document initiator)
        {
            if (_redo.Count == 0) return false;
            var record = RemoveLast(_redo);
            _undo.Add(record);
            _tree = record.NewTree;
            Publish(initiator,
                ReferenceEquals(record.Origin, initiator) ? record.NewCaretSet : null,
                DocumentChangeKind.Redo, record.Change);
            return true;
        }

        public void Reset(string text, Document initiator, TextCaretSet caretSet)
        {
            var oldSnapshot = Snapshot;
            var change = new TextChange(new TextRange(0, oldSnapshot.Length), oldSnapshot.Text, text);
            _tree = new TextTree(text);
            Snapshot = new TextSnapshot(_tree);
            _undo.Clear();
            _redo.Clear();
            Changed?.Invoke(this, new BufferChangedEventArgs(
                oldSnapshot, Snapshot, change, initiator, caretSet, DocumentChangeKind.Edit));
        }

        public void ClearHistory() { _undo.Clear(); _redo.Clear(); }

        private void Publish(Document initiator, TextCaretSet? carets, DocumentChangeKind kind, TextChange change)
        {
            var oldSnapshot = Snapshot;
            Snapshot = new TextSnapshot(_tree);
            Changed?.Invoke(this, new BufferChangedEventArgs(
                oldSnapshot, Snapshot, change, initiator, carets, kind));
        }

        private static EditRecord RemoveLast(List<EditRecord> records)
        {
            var index = records.Count - 1;
            var record = records[index];
            records.RemoveAt(index);
            return record;
        }
    }

    private sealed class BufferChangedEventArgs : EventArgs
    {
        public BufferChangedEventArgs(
            TextSnapshot oldSnapshot,
            TextSnapshot newSnapshot,
            TextChange change,
            Document initiator,
            TextCaretSet? initiatorCaretSet,
            DocumentChangeKind kind)
        {
            OldSnapshot = oldSnapshot;
            NewSnapshot = newSnapshot;
            Change = change;
            Initiator = initiator;
            InitiatorCaretSet = initiatorCaretSet;
            Kind = kind;
        }

        public TextSnapshot OldSnapshot { get; }
        public TextSnapshot NewSnapshot { get; }
        public TextChange Change { get; }
        public Document Initiator { get; }
        public TextCaretSet? InitiatorCaretSet { get; }
        public DocumentChangeKind Kind { get; }
    }

    private sealed record EditRecord(
        TextChange Change,
        TextTree OldTree,
        TextTree NewTree,
        Document Origin,
        TextCaretSet OldCaretSet,
        TextCaretSet NewCaretSet)
    {
        public TextSelection OldSelection => OldCaretSet.Primary.Selection;

        public TextSelection NewSelection => NewCaretSet.Primary.Selection;
    }
}
