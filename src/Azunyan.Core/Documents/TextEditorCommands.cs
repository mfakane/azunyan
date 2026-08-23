namespace Azunyan.Core;

/// <summary>
/// Small, UI-independent editing commands shared by text controls. The
/// commands keep the document's selection direction and operate on grapheme
/// cluster boundaries, so an emoji or combining sequence is never split by a
/// normal caret move or deletion.
/// </summary>
public static class TextEditorCommands
{
    public static void MoveCaretByGrapheme(Document document, int count, bool extendSelection = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (count == 0)
        {
            return;
        }

        var selection = document.Selection;
        var position = extendSelection || selection.IsEmpty
            ? selection.CaretPosition
            : count < 0 ? selection.Start : selection.End;
        var target = UnicodeText.MoveByTextElements(document.Text, position, count);
        if (extendSelection)
        {
            document.SetSelection(selection.Anchor, target);
        }
        else
        {
            document.SetCaret(target);
        }
    }

    public static void MoveCaretByScalar(Document document, int count, bool extendSelection = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (count == 0)
        {
            return;
        }

        var selection = document.Selection;
        var position = extendSelection || selection.IsEmpty
            ? selection.CaretPosition
            : count < 0 ? selection.Start : selection.End;
        var target = UnicodeText.MoveByScalars(document.Text, position, count);
        if (extendSelection)
        {
            document.SetSelection(selection.Anchor, target);
        }
        else
        {
            document.SetCaret(target);
        }
    }

    public static TextChange DeleteBackward(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var range = document.Selection.IsEmpty
            ? UnicodeText.GetBackwardDeleteRange(document.Text, document.CaretPosition)
            : document.Selection.Range;
        return document.Delete(range);
    }

    public static TextChange DeleteForward(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var range = document.Selection.IsEmpty
            ? UnicodeText.GetForwardDeleteRange(document.Text, document.CaretPosition)
            : document.Selection.Range;
        return document.Delete(range);
    }

    public static TextChange DeleteBackwardByScalar(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var range = document.Selection.IsEmpty
            ? UnicodeText.GetBackwardScalarDeleteRange(document.Text, document.CaretPosition)
            : document.Selection.Range;
        return document.Delete(range);
    }

    public static TextChange DeleteForwardByScalar(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var range = document.Selection.IsEmpty
            ? UnicodeText.GetForwardScalarDeleteRange(document.Text, document.CaretPosition)
            : document.Selection.Range;
        return document.Delete(range);
    }
}
