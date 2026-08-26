using Azunyan.Core;

namespace Azunote;

/// <summary>
/// Captures editor text and positions against one immutable snapshot. Native
/// editor selection events can briefly expose positions from before or after
/// the snapshot returned by the editor, so all positions are normalized before
/// asking the line index for a location.
/// </summary>
internal readonly record struct EditorBufferSnapshot(
    TextSnapshot Snapshot,
    TextSelection Selection,
    string Text,
    string SelectedText,
    LineColumn Caret,
    LineColumn SelectionStart,
    LineColumn SelectionEnd)
{
    public static EditorBufferSnapshot Capture(IEditorBuffer editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var snapshot = editor.Snapshot;
        var rawSelection = editor.Selection;
        var anchor = Math.Clamp(rawSelection.Anchor, 0, snapshot.Length);
        var active = Math.Clamp(rawSelection.Active, 0, snapshot.Length);
        var selection = new TextSelection(anchor, active);
        var lines = snapshot.Lines;

        return new EditorBufferSnapshot(
            snapshot,
            selection,
            snapshot.Text,
            snapshot.GetText(selection.Range),
            lines.GetLineColumn(selection.CaretPosition),
            lines.GetLineColumn(selection.Start),
            lines.GetLineColumn(selection.End));
    }
}
