using Azunyan.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Azunyan.WinUI;

/// <summary>
/// The WinUI editor surface. It deliberately derives from the platform
/// TextBox: Windows supplies the text-service/IME connection, candidate-window
/// anchoring, selection rendering, scrolling, and Edit UI Automation patterns.
/// The core <see cref="Document"/> remains the source of editing semantics.
/// </summary>
public sealed partial class AzunyanEditorControl : TextBox
{
    private bool _synchronizing;
    private TextRange? _compositionRange;

    public AzunyanEditorControl()
    {
        Document = new Document();
        Document.Changed += OnDocumentChanged;
        IsSpellCheckEnabled = false;
        IsTextPredictionEnabled = false;
        AutomationProperties.SetName(this, "Text editor");
        AutomationProperties.SetHelpText(this, "Azunyan document editor");

        TextChanged += OnTextChanged;
        SelectionChanged += OnSelectionChanged;
        KeyDown += OnKeyDown;
        TextCompositionStarted += OnTextCompositionStarted;
        TextCompositionChanged += OnTextCompositionChanged;
        TextCompositionEnded += OnTextCompositionEnded;
    }

    public Document Document { get; private set; }

    public TextSnapshot Snapshot => Document.Snapshot;

    public bool IsComposing => _compositionRange is not null;

    public TextRange? CompositionRange => _compositionRange;

    /// <summary>
    /// Gets or sets whether a normal Enter inserts a line break with copied
    /// leading indentation. Hosts can temporarily disable this while another
    /// Enter action, such as completion acceptance, owns the key.
    /// </summary>
    public bool AutoIndentOnEnter { get; set; } = true;

    /// <summary>
    /// Raised when the native text service starts, updates, or ends an IME
    /// composition. The range is expressed in the current native text and is
    /// forwarded to the projected renderer as transient decoration state.
    /// </summary>
    public event EventHandler? CompositionChanged;

    /// <summary>
    /// Raised after the native text service has been mirrored into the core
    /// document. Consumers can use the coarse change to invalidate projected
    /// layout state without diffing the full text again.
    /// </summary>
    public event EventHandler<DocumentChangedEventArgs>? DocumentChanged;

    /// <summary>
    /// Replaces the displayed document and starts a fresh undo history. This
    /// is used for opening and creating files; ordinary user edits continue to
    /// flow through the native text service and are mirrored into Document.
    /// </summary>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        ClearComposition();
        _synchronizing = true;
        try
        {
            Document.Changed -= OnDocumentChanged;
            Document = new Document(text);
            Document.Changed += OnDocumentChanged;
            Text = text;
            SelectionStart = 0;
            SelectionLength = 0;
        }
        finally
        {
            _synchronizing = false;
        }
    }

    public void SetDocumentSelection(TextSelection selection)
    {
        if (selection.Start > Document.Length || selection.End > Document.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(selection));
        }

        _synchronizing = true;
        try
        {
            Document.Selection = selection;
            SelectionStart = selection.Start;
            SelectionLength = selection.Length;
        }
        finally
        {
            _synchronizing = false;
        }
    }

    /// <summary>
    /// Inserts a line break and carries the current line's leading whitespace
    /// onto the new line. The caller is responsible for deciding whether the
    /// key event should be treated as a normal Enter (for example, completion
    /// popups may consume Enter first).
    /// </summary>
    public TextChange InsertNewLineWithAutoIndent()
    {
        SyncDocumentSelection();
        ClearComposition();
        var change = TextEditorCommands.InsertNewLineWithAutoIndent(Document);
        ApplyDocumentState();
        return change;
    }

    /// <summary>
    /// Applies one document replacement and updates the native text-service
    /// host and document selection as one operation. This is required for
    /// programmatic edits such as completion acceptance: setting
    /// <see cref="TextBox.SelectedText"/> first would raise <c>TextChanged</c>
    /// after the caller tried to place the new caret.
    /// </summary>
    public void ReplaceDocumentRange(TextRange range, string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (range.Start > Document.Length || range.End > Document.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(range));
        }

        _synchronizing = true;
        try
        {
            ClearComposition();
            Document.Replace(range, replacement);
            Text = Document.Text;
            ApplyDocumentSelection();
        }
        finally
        {
            _synchronizing = false;
        }
    }

    public bool UndoDocument()
    {
        SyncDocumentSelection();
        if (!Document.Undo())
        {
            return false;
        }

        ApplyDocumentState();
        return true;
    }

    public bool RedoDocument()
    {
        SyncDocumentSelection();
        if (!Document.Redo())
        {
            return false;
        }

        ApplyDocumentState();
        return true;
    }

    private void OnTextChanged(object sender, TextChangedEventArgs args)
    {
        if (_synchronizing)
        {
            return;
        }

        var currentText = Text;
        var previousText = Document.Text;
        if (!string.Equals(previousText, currentText, StringComparison.Ordinal))
        {
            var (range, insertedText) = FindReplacement(previousText, currentText);
            var replacementRange = range;
            var indentationPosition = range.Start;
            var autoIndentLineBreak = AutoIndentOnEnter
                && !IsComposing
                && IsLineBreakWithOptionalIndentation(insertedText);

            if (autoIndentLineBreak)
            {
                replacementRange = TextEditorCommands.GetNewLineReplacementRange(
                    Document.Snapshot,
                    range);
            }

            if (autoIndentLineBreak
                && TryGetNativeClosingLineBreakInsertion(
                    Document.Snapshot,
                    range,
                    out var lineBreakPosition))
            {
                // The native TextBox can report Enter at the start of an
                // existing closing-delimiter line. Keep that line intact and
                // insert the new indented line before its original ending.
                replacementRange = TextRange.Empty(lineBreakPosition);
                indentationPosition = lineBreakPosition;
            }

            var replacement = autoIndentLineBreak
                ? TextEditorCommands.GetNewLineWithAutoIndentation(
                    Document.Snapshot,
                    indentationPosition)
                : insertedText;

            if (AutoIndentOnEnter
                && !IsComposing
                && insertedText.Length == 1
                && TextEditorCommands.TryGetClosingDelimiterDedent(
                    Document.Snapshot,
                    range.End,
                    insertedText[0],
                    out var indentationRange,
                    out var targetIndentation))
            {
                var isNativeIndentationReplacement =
                    !range.IsEmpty
                    && range.Start == indentationRange.Start
                    && range.End == indentationRange.End;
                if (range.IsEmpty || isNativeIndentationReplacement)
                {
                    replacementRange = indentationRange;
                    replacement = targetIndentation + insertedText;
                }
            }

            Document.Replace(replacementRange, replacement);

            if (!string.Equals(Text, Document.Text, StringComparison.Ordinal))
            {
                ApplyDocumentState();
                return;
            }
        }

        SyncDocumentSelection();
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs args) =>
        DocumentChanged?.Invoke(this, args);

    private void OnSelectionChanged(object sender, RoutedEventArgs args)
    {
        if (!_synchronizing)
        {
            SyncDocumentSelection();
        }
    }

    private void OnTextCompositionStarted(
        object sender,
        TextCompositionStartedEventArgs args) =>
        SetComposition(args.StartIndex, args.Length);

    private void OnTextCompositionChanged(
        object sender,
        TextCompositionChangedEventArgs args) =>
        SetComposition(args.StartIndex, args.Length);

    private void OnTextCompositionEnded(
        object sender,
        TextCompositionEndedEventArgs args) =>
        ClearComposition();

    private void SetComposition(int start, int length)
    {
        var safeStart = Math.Clamp(start, 0, Text.Length);
        var safeLength = Math.Clamp(length, 0, Text.Length - safeStart);
        _compositionRange = new TextRange(safeStart, safeLength);
        CompositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearComposition()
    {
        if (_compositionRange is null)
        {
            return;
        }

        _compositionRange = null;
        CompositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        var control = IsKeyDown(VirtualKey.Control);
        var menu = IsKeyDown(VirtualKey.Menu);
        var extendSelection = IsKeyDown(VirtualKey.Shift);
        switch (args.Key)
        {
            case VirtualKey.Enter
                when AutoIndentOnEnter
                && AcceptsReturn
                && !IsComposing
                && !control
                && !menu:
                InsertNewLineWithAutoIndent();
                args.Handled = true;
                break;
            case VirtualKey.Tab
                when !IsComposing
                && !control
                && !menu:
                SyncDocumentSelection();
                TextEditorCommands.IndentSelection(Document, extendSelection);
                ApplyDocumentState();
                args.Handled = true;
                break;
            case VirtualKey.Z when control && !IsComposing:
                if (extendSelection)
                {
                    RedoDocument();
                }
                else
                {
                    UndoDocument();
                }

                args.Handled = true;
                break;
            case VirtualKey.Y when control && !IsComposing:
                RedoDocument();
                args.Handled = true;
                break;
            case VirtualKey.Left when !IsComposing:
                SyncDocumentSelection();
                Document.MoveCaretByGrapheme(-1, extendSelection);
                ApplyDocumentSelection();
                args.Handled = true;
                break;
            case VirtualKey.Right when !IsComposing:
                SyncDocumentSelection();
                Document.MoveCaretByGrapheme(1, extendSelection);
                ApplyDocumentSelection();
                args.Handled = true;
                break;
            case VirtualKey.Delete when !IsComposing:
                SyncDocumentSelection();
                Document.DeleteForward();
                ApplyDocumentState();
                args.Handled = true;
                break;
        }
    }

    private void SyncDocumentSelection()
    {
        var start = Math.Clamp(SelectionStart, 0, Document.Length);
        var length = Math.Clamp(SelectionLength, 0, Document.Length - start);
        Document.Selection = new TextSelection(start, start + length);
    }

    private void ApplyDocumentState()
    {
        _synchronizing = true;
        try
        {
            Text = Document.Text;
            ApplyDocumentSelection();
        }
        finally
        {
            _synchronizing = false;
        }
    }

    private void ApplyDocumentSelection()
    {
        var wasSynchronizing = _synchronizing;
        _synchronizing = true;
        try
        {
            SelectionStart = Document.Selection.Start;
            SelectionLength = Document.Selection.Length;
        }
        finally
        {
            _synchronizing = wasSynchronizing;
        }
    }

    private static bool IsKeyDown(VirtualKey key)
    {
        var state = InputKeyboardSource.GetKeyStateForCurrentThread(key);
        return state.HasFlag(CoreVirtualKeyStates.Down);
    }

    private static bool IsLineBreakWithOptionalIndentation(string text)
    {
        if (!TryGetLineBreakLength(text, out var lineBreakLength))
        {
            return false;
        }

        for (var index = lineBreakLength; index < text.Length; index++)
        {
            if (text[index] is not (' ' or '\t'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetNativeClosingLineBreakInsertion(
        TextSnapshot snapshot,
        TextRange range,
        out int insertionPosition)
    {
        insertionPosition = 0;
        if (range.Start >= snapshot.Length)
        {
            return false;
        }

        var line = snapshot.Lines.GetLine(range.Start);
        var lineStart = snapshot.Lines.GetLineStart(line);
        if (range.Start != lineStart)
        {
            return false;
        }

        var lineEnd = snapshot.Lines.GetLineEnd(line);
        var text = snapshot.Text;
        var firstCodePosition = range.Start;
        while (firstCodePosition < lineEnd
            && text[firstCodePosition] is ' ' or '\t')
        {
            firstCodePosition++;
        }

        if (firstCodePosition >= lineEnd
            || (!range.IsEmpty && range.End > firstCodePosition)
            || !IsClosingDelimiter(text[firstCodePosition])
            || !TryGetLineEndingLengthBefore(
                text,
                range.Start,
                out var lineEndingLength))
        {
            return false;
        }

        insertionPosition = range.Start - lineEndingLength;
        return true;
    }

    private static bool TryGetLineBreakLength(string text, out int length)
    {
        if (text.StartsWith("\r\n", StringComparison.Ordinal))
        {
            length = 2;
            return true;
        }

        if (text.Length > 0 && text[0] is ('\r' or '\n'))
        {
            length = 1;
            return true;
        }

        length = 0;
        return false;
    }

    private static bool TryGetLineEndingLengthBefore(
        string text,
        int position,
        out int length)
    {
        if (position >= 2
            && text[position - 2] == '\r'
            && text[position - 1] == '\n')
        {
            length = 2;
            return true;
        }

        if (position >= 1 && text[position - 1] is '\r' or '\n')
        {
            length = 1;
            return true;
        }

        length = 0;
        return false;
    }

    private static bool IsClosingDelimiter(char value) => value is '}' or ']' or ')';

    private static (TextRange Range, string InsertedText) FindReplacement(string previousText, string currentText)
    {
        var prefixLength = 0;
        var commonLength = Math.Min(previousText.Length, currentText.Length);
        while (prefixLength < commonLength && previousText[prefixLength] == currentText[prefixLength])
        {
            prefixLength++;
        }

        var suffixLength = 0;
        while (suffixLength < previousText.Length - prefixLength
            && suffixLength < currentText.Length - prefixLength
            && previousText[previousText.Length - suffixLength - 1]
                == currentText[currentText.Length - suffixLength - 1])
        {
            suffixLength++;
        }

        var oldLength = previousText.Length - prefixLength - suffixLength;
        var newLength = currentText.Length - prefixLength - suffixLength;
        return (
            new TextRange(prefixLength, oldLength),
            currentText.Substring(prefixLength, newLength));
    }
}
