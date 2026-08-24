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

    /// <summary>
    /// Inserts a line break at the current selection and carries the current
    /// line's leading spaces and tabs onto the new line. The existing document
    /// line-ending style is preserved when one is present.
    /// </summary>
    public static TextChange InsertNewLineWithAutoIndent(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var selection = document.Selection;
        return document.Replace(
            selection.Range,
            GetNewLineWithAutoIndentation(document.Snapshot, selection.Start));
    }

    /// <summary>
    /// Returns a line break followed by the indentation that should be copied
    /// from the line containing <paramref name="position"/>.
    /// </summary>
    public static string GetNewLineWithAutoIndentation(TextSnapshot snapshot, int position)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return GetPreferredLineEnding(snapshot.Text)
            + GetLineIndentation(snapshot, position);
    }

    /// <summary>
    /// Returns the leading spaces and tabs of the line containing
    /// <paramref name="position"/>.
    /// </summary>
    public static string GetLineIndentation(TextSnapshot snapshot, int position)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var line = snapshot.Lines.GetLine(position);
        var start = snapshot.Lines.GetLineStart(line);
        var end = snapshot.Lines.GetLineEnd(line);
        var text = snapshot.Text;
        var indentationEnd = start;
        while (indentationEnd < end
            && (text[indentationEnd] == ' ' || text[indentationEnd] == '\t'))
        {
            indentationEnd++;
        }

        return text[start..indentationEnd];
    }

    private static string GetPreferredLineEnding(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '\r':
                    return index + 1 < text.Length && text[index + 1] == '\n'
                        ? "\r\n"
                        : "\r";
                case '\n':
                    return "\n";
            }
        }

        return Environment.NewLine;
    }
}
