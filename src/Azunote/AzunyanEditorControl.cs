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

    public AzunyanEditorControl()
    {
        Document = new Document();
        IsSpellCheckEnabled = false;
        IsTextPredictionEnabled = false;
        AutomationProperties.SetName(this, "Text editor");
        AutomationProperties.SetHelpText(this, "Azunote document editor");

        TextChanged += OnTextChanged;
        SelectionChanged += OnSelectionChanged;
        KeyDown += OnKeyDown;
    }

    public Document Document { get; private set; }

    public TextSnapshot Snapshot => Document.Snapshot;

    /// <summary>
    /// Replaces the displayed document and starts a fresh undo history. This
    /// is used for opening and creating files; ordinary user edits continue to
    /// flow through the native text service and are mirrored into Document.
    /// </summary>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        _synchronizing = true;
        try
        {
            Document = new Document(text);
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

    private void OnSelectionChanged(object sender, RoutedEventArgs args)
    {
        if (!_synchronizing)
        {
            SyncDocumentSelection();
        }
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
