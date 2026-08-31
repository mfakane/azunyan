using Azunyan.Core;

namespace Azunote;

internal sealed class DocumentStatusPresenter
{
    private readonly IEditorView _editor;
    private readonly DocumentSession _session;
    private readonly IStatusBarView _statusBar;
    private readonly IWindowChromeView _window;
    private readonly Func<string> _languageModeDisplayName;

    public DocumentStatusPresenter(
        IEditorView editor,
        DocumentSession session,
        IStatusBarView statusBar,
        IWindowChromeView window,
        Func<string>? languageModeDisplayName = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _statusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _languageModeDisplayName = languageModeDisplayName ?? (() => "Plain Text");
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
                selectionStart,
                _editor.IndentSize,
                _editor.TabDisplaySize,
                _editor.IndentationInputMode).DisplayName,
            _languageModeDisplayName(),
            _session.State.FilePath ?? "Untitled",
            _session.State.FilePath is not null);
        _statusBar.Apply(state);
    }

    public void RefreshTitle()
    {
        var name = _session.State.FilePath is null
            ? "Untitled"
            : Path.GetFileName(_session.State.FilePath);
        var dirtyMarker = _session.State.IsDirty ? " *" : string.Empty;
        _window.SetTitle($"{name}{dirtyMarker} - Azunote");
    }
}
