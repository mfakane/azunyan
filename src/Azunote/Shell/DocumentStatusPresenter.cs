using Azunyan.Core;

namespace Azunote;

internal sealed class DocumentStatusPresenter
{
    private readonly IEditorBuffer _editor;
    private readonly DocumentSession _session;
    private readonly IStatusBarView _statusBar;
    private readonly IWindowChromeView _window;

    public DocumentStatusPresenter(
        IEditorBuffer editor,
        DocumentSession session,
        IStatusBarView statusBar,
        IWindowChromeView window)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _statusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public void Refresh(LineEndingKind? lineEnding = null)
    {
        var snapshot = _editor.Snapshot;
        var text = snapshot.Text;
        var selectionStart = Math.Clamp(_editor.Selection.Start, 0, text.Length);
        var lineColumn = snapshot.Lines.GetLineColumn(selectionStart);
        var state = new StatusBarState(
            $"Ln {lineColumn.Line + 1}, Col {lineColumn.Column + 1}",
            TextFileService.GetEncodingDisplayName(_session.State.Encoding),
            TextFileService.GetLineEndingDisplayName(
                lineEnding ?? _session.State.LineEnding),
            TextEditorCommands.GetIndentationSettings(
                snapshot,
                selectionStart).DisplayName,
            _session.State.FilePath ?? "Untitled");
        _statusBar.Apply(state);
    }

    public void RefreshTitle()
    {
        var name = _session.State.FilePath is null
            ? "Untitled"
            : Path.GetFileName(_session.State.FilePath);
        var dirtyMarker = _session.State.IsDirty ? "*" : string.Empty;
        _window.SetTitle($"{dirtyMarker}{name} — Azunote");
    }
}
