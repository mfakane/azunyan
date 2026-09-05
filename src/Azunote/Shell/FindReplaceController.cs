namespace Azunote;

internal sealed class FindReplaceController
{
    private readonly IEditorView _editor;
    private readonly IFindReplaceState _state;
    private readonly IFindReplaceHost _host;
    private readonly Action _refreshDocumentView;
    private readonly Action _observeText;

    public FindReplaceController(
        IEditorView editor,
        IFindReplaceState state,
        IFindReplaceHost host,
        Action refreshDocumentView,
        Action observeText)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _refreshDocumentView = refreshDocumentView ?? throw new ArgumentNullException(nameof(refreshDocumentView));
        _observeText = observeText ?? throw new ArgumentNullException(nameof(observeText));
    }

    public void Show(bool replace)
    {
        _state.Show();
        _host.FocusFind();
        if (_editor.Selection.Length > 0 && !string.IsNullOrEmpty(_editor.SelectedText))
        {
            _state.FindText = _editor.SelectedText;
            _host.SelectFindText();
        }

        if (!replace)
        {
            _state.ReplaceText = string.Empty;
        }
    }

    public void Close()
    {
        _state.Close();
        _host.FocusEditor();
    }

    public void OnFindTextChanged()
    {
        if (_state.IsVisible)
        {
            _state.SetResult(string.Empty);
        }
    }

    public void FindNext()
    {
        var query = _state.FindText;
        if (string.IsNullOrEmpty(query))
        {
            _state.SetResult("Enter search text");
            return;
        }

        var selection = _editor.Selection;
        var match = FindReplaceService.FindNext(
            _editor.Text,
            query,
            selection.Start + selection.Length);
        if (match is not { } found)
        {
            _state.SetResult("Not found");
            return;
        }

        _editor.Focus();
        _editor.SetSelection(new Azunyan.Core.TextSelection(
            found.Start,
            found.Start + found.Length));
        _state.SetResult("Found");
    }

    public void ReplaceCurrent()
    {
        var query = _state.FindText;
        if (string.IsNullOrEmpty(query))
        {
            _state.SetResult("Enter search text");
            return;
        }

        var selection = _editor.Selection;
        if (!selection.IsEmpty
            && FindReplaceService.IsMatch(_editor.SelectedText, query))
        {
            var replacement = _state.ReplaceText;
            _editor.Replace(selection.Range, replacement);
            _editor.SetSelection(new Azunyan.Core.TextSelection(
                selection.Start,
                selection.Start + replacement.Length));
            _observeText();
            _refreshDocumentView();
            FindNext();
            return;
        }

        FindNext();
    }

    public void ReplaceAll()
    {
        var query = _state.FindText;
        if (string.IsNullOrEmpty(query))
        {
            _state.SetResult("Enter search text");
            return;
        }

        var count = FindReplaceService.Count(_editor.Text, query);
        if (count > 0)
        {
            _editor.SetText(FindReplaceService.ReplaceAll(
                _editor.Text,
                query,
                _state.ReplaceText));
            _observeText();
            _refreshDocumentView();
        }

        _state.SetResult($"{count} replaced");
    }
}
