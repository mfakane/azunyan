using Azunyan.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Azunote;

/// <summary>
/// The WinUI editor surface. It deliberately derives from the platform
/// TextBox: Windows supplies the text-service/IME connection, candidate-window
/// anchoring, selection rendering, scrolling, and Edit UI Automation patterns.
/// The core <see cref="Document"/> remains the source of editing semantics.
/// </summary>
public sealed class AzunyanEditorControl : TextBox
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
        AutomationProperties.SetHelpText(this, "Azunote document editor");

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
            Document.Replace(range, insertedText);
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
        var extendSelection = IsKeyDown(VirtualKey.Shift);
        switch (args.Key)
        {
            case VirtualKey.Z when control:
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
            case VirtualKey.Y when control:
                RedoDocument();
                args.Handled = true;
                break;
            case VirtualKey.Left:
                Document.MoveCaretByGrapheme(-1, extendSelection);
                ApplyDocumentSelection();
                args.Handled = true;
                break;
            case VirtualKey.Right:
                Document.MoveCaretByGrapheme(1, extendSelection);
                ApplyDocumentSelection();
                args.Handled = true;
                break;
            case VirtualKey.Back:
                Document.DeleteBackward();
                ApplyDocumentState();
                args.Handled = true;
                break;
            case VirtualKey.Delete:
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
