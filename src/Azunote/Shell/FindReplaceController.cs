namespace Azunote;

internal sealed class FindReplaceController
{
    private readonly IEditorView _editor;
    private readonly IFindReplaceView _view;
    private readonly Action _refreshDocumentView;
    private readonly Action _observeText;

    public FindReplaceController(
        IEditorView editor,
        IFindReplaceView view,
        Action refreshDocumentView,
        Action observeText)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _refreshDocumentView = refreshDocumentView ?? throw new ArgumentNullException(nameof(refreshDocumentView));
        _observeText = observeText ?? throw new ArgumentNullException(nameof(observeText));
    }

    public void Show(bool replace) => _view.Show(replace);

    public void Close() => _view.Close();

    public void OnFindTextChanged()
    {
        if (_view.IsVisible)
        {
            _view.SetResult(string.Empty);
        }
    }

    public void FindNext()
    {
        var query = _view.FindText;
        if (string.IsNullOrEmpty(query))
        {
            _view.SetResult("Enter search text");
            return;
        }

        var selection = _editor.Selection;
        var match = FindReplaceService.FindNext(
            _editor.Text,
            query,
            selection.Start + selection.Length);
        if (match is not { } found)
        {
            _view.SetResult("Not found");
            return;
        }

        _editor.Focus();
        _editor.SetSelection(new Azunyan.Core.TextSelection(
            found.Start,
            found.Start + found.Length));
        _view.SetResult("Found");
    }

    public void ReplaceCurrent()
    {
        var query = _view.FindText;
        if (string.IsNullOrEmpty(query))
        {
            _view.SetResult("Enter search text");
            return;
        }

        var selection = _editor.Selection;
        if (!selection.IsEmpty
            && FindReplaceService.IsMatch(_editor.SelectedText, query))
        {
            var replacement = _view.ReplaceText;
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
        var query = _view.FindText;
        if (string.IsNullOrEmpty(query))
        {
            _view.SetResult("Enter search text");
            return;
        }

        var count = FindReplaceService.Count(_editor.Text, query);
        if (count > 0)
        {
            _editor.SetText(FindReplaceService.ReplaceAll(
                _editor.Text,
                query,
                _view.ReplaceText));
            _observeText();
            _refreshDocumentView();
        }

        _view.SetResult($"{count} replaced");
    }
}
