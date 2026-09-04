namespace Azunote;

internal enum SearchDirection
{
    Forward,
    Backward
}

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

    public void ToggleMode() => _view.ToggleMode();

    public void Close() => _view.Close();

    public void OnFindTextChanged()
    {
        _view.HideNotification();
        if (_view.IsVisible)
        {
            _view.SetResult(0, 0);
        }
    }

    public void OnFindOptionsChanged() => OnFindTextChanged();

    public void FindNext()
        => Find(SearchDirection.Forward);

    public void FindPrevious()
        => Find(SearchDirection.Backward);

    private void Find(SearchDirection direction)
    {
        _view.HideNotification();
        _view.SetResult(0, 0);
        var query = NormalizeLineEndingsForDocument(_view.FindText);
        if (string.IsNullOrEmpty(query))
        {
            return;
        }

        if (!FindReplaceService.IsValidQuery(query, _view.Options))
        {
            _view.ShowNotification("Invalid regular expression");
            return;
        }

        var selection = _editor.Selection;
        var result = direction == SearchDirection.Forward
            ? FindReplaceService.FindNextResult(
                _editor.Text,
                query,
                selection.Start + selection.Length,
                _view.Options)
            : FindReplaceService.FindPreviousResult(
                _editor.Text,
                query,
                selection.Start,
                _view.Options);
        if (result is not { } foundResult)
        {
            _view.SetResult(0, 0);
            return;
        }

        var totalMatches = FindReplaceService.Count(
            _editor.Text,
            query,
            _view.Options);
        var matchNumber = FindReplaceService.CountBefore(
            _editor.Text,
            query,
            foundResult.Match.Start,
            _view.Options) + 1;
        _editor.Focus();
        _editor.SetSelection(new Azunyan.Core.TextSelection(
            foundResult.Match.Start,
            foundResult.Match.End));
        _view.SetResult(matchNumber, totalMatches);
        if (foundResult.Wrapped)
        {
            _view.ShowNotification(direction == SearchDirection.Forward
                ? "Search wrapped to the beginning"
                : "Search wrapped to the end");
        }
    }

    public void ReplaceCurrent()
    {
        _view.HideNotification();
        _view.SetResult(0, 0);
        var query = NormalizeLineEndingsForDocument(_view.FindText);
        if (string.IsNullOrEmpty(query))
        {
            return;
        }

        if (!FindReplaceService.IsValidQuery(query, _view.Options))
        {
            _view.ShowNotification("Invalid regular expression");
            return;
        }

        var selection = _editor.Selection;
        if (!selection.IsEmpty
            && FindReplaceService.IsMatch(_editor.SelectedText, query, _view.Options))
        {
            var replacement = NormalizeLineEndingsForDocument(_view.ReplaceText);
            var replacedText = FindReplaceService.ReplaceMatch(
                _editor.SelectedText,
                query,
                replacement,
                _view.Options);
            _editor.Replace(selection.Range, replacedText);
            _editor.SetSelection(new Azunyan.Core.TextSelection(
                selection.Start,
                selection.Start + replacedText.Length));
            _observeText();
            _refreshDocumentView();
            Find(SearchDirection.Forward);
            return;
        }

        Find(SearchDirection.Forward);
    }

    public void ReplaceAll()
    {
        _view.HideNotification();
        _view.SetResult(0, 0);
        var query = NormalizeLineEndingsForDocument(_view.FindText);
        if (string.IsNullOrEmpty(query))
        {
            return;
        }

        if (!FindReplaceService.IsValidQuery(query, _view.Options))
        {
            _view.ShowNotification("Invalid regular expression");
            return;
        }

        var count = FindReplaceService.Count(_editor.Text, query, _view.Options);
        if (count > 0)
        {
            _editor.SetText(FindReplaceService.ReplaceAll(
                _editor.Text,
                query,
                NormalizeLineEndingsForDocument(_view.ReplaceText),
                _view.Options));
            _observeText();
            _refreshDocumentView();
        }

        _view.SetResult(0, count);
    }

    private string NormalizeLineEndingsForDocument(string text)
    {
        var documentText = _editor.Text;
        var lineEnding = documentText.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : documentText.Contains('\r') ? "\r" : "\n";
        return text.ReplaceLineEndings(lineEnding);
    }
}
