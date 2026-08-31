using System.Globalization;
using Azunyan.Core;

namespace Azunote;

internal sealed class GoToLineController
{
    private readonly IEditorView _editor;
    private readonly IGoToLineDialog _dialog;

    public GoToLineController(IEditorView editor, IGoToLineDialog dialog)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
    }

    public async Task ShowAsync()
    {
        var snapshot = _editor.Snapshot;
        var caretPosition = Math.Clamp(_editor.CaretPosition, 0, snapshot.Length);
        var current = snapshot.Lines.GetLineColumn(caretPosition);
        var initialText = string.Create(
            CultureInfo.InvariantCulture,
            $"{current.Line + 1}:{current.Column + 1}");
        var target = await _dialog.ShowAsync(initialText);
        if (target is not { } requested)
        {
            return;
        }

        _editor.SetPosition(GoToLineService.Resolve(snapshot, requested));
        _editor.Focus();
    }
}
