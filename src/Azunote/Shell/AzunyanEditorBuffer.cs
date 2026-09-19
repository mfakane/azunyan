using Azunyan.Core;
using Azunyan.WinUI;

namespace Azunote;

internal sealed class AzunyanEditorBuffer : IEditorBuffer
{
    private readonly AzunyanEditorView _editor;

    public AzunyanEditorBuffer(AzunyanEditorView editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _editor.DocumentChanged += (_, e) => Edited?.Invoke(this, e.Change);
    }

    public event EventHandler<TextChange>? Edited;

    public string Text => _editor.Text;

    public string SelectedText => _editor.SelectedText;

    public TextSnapshot Snapshot => _editor.Snapshot;

    public TextSelection Selection =>
        new(_editor.SelectionStart, _editor.SelectionStart + _editor.SelectionLength);

    public int CaretPosition => _editor.Document.CaretPosition;

    public void SetText(string text) => _editor.SetText(text);

    public void SetSelection(TextSelection selection) => _editor.SetDocumentSelection(selection);

    public void Replace(TextRange range, string replacement) =>
        _editor.ReplaceDocumentRange(range, replacement);
}
