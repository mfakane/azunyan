namespace Azunote;

internal enum SearchDirection
{
    Forward,
    Backward
}

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
        _state.Show(replace);
        _host.HideNotification();
        _state.SetResult(0, 0);
        _host.FocusFind();
        if (_editor.Selection.Length > 0 && !string.IsNullOrEmpty(_editor.SelectedText))
        {
            _state.FindText = _editor.SelectedText;
            _host.SelectFindText();
        }
    }

    public void ToggleMode() => _state.ToggleMode();

    public void Close()
    {
        _state.Close();
        _host.HideNotification();
        _host.FocusEditor();
    }

    public void OnFindTextChanged()
    {
        _host.HideNotification();
        if (_state.IsVisible)
        {
            _state.SetResult(0, 0);
        }
    }

    public void OnFindOptionsChanged() => OnFindTextChanged();

    public void FindNext()
        => Find(SearchDirection.Forward);

    public void FindPrevious()
        => Find(SearchDirection.Backward);

    private void Find(SearchDirection direction)
    {
        _host.HideNotification();
        _state.SetResult(0, 0);
        var query = NormalizeLineEndingsForDocument(_state.FindText);
        if (string.IsNullOrEmpty(query))
        {
            return;
        }

        if (!FindReplaceService.IsValidQuery(query, _state.Options))
        {
            _host.ShowNotification("Invalid regular expression", _state.IsReplaceMode);
            return;
        }

        var selection = _editor.Selection;
        var result = direction == SearchDirection.Forward
            ? FindReplaceService.FindNextResult(
                _editor.Text,
                query,
                selection.Start + selection.Length,
                _state.Options)
            : FindReplaceService.FindPreviousResult(
                _editor.Text,
                query,
                selection.Start,
                _state.Options);
        if (result is not { } foundResult)
        {
            _state.SetResult(0, 0);
            return;
        }

        var totalMatches = FindReplaceService.Count(
            _editor.Text,
            query,
            _state.Options);
        var matchNumber = FindReplaceService.CountBefore(
            _editor.Text,
            query,
            foundResult.Match.Start,
            _state.Options) + 1;
        _editor.SetSelection(new Azunyan.Core.TextSelection(
            foundResult.Match.Start,
            foundResult.Match.End));
        _editor.ScrollSelectionIntoView();
        _state.SetResult(matchNumber, totalMatches);
        if (foundResult.Wrapped)
        {
            _host.ShowNotification(direction == SearchDirection.Forward
                ? "Search wrapped to the beginning"
                : "Search wrapped to the end", _state.IsReplaceMode);
        }
    }

    public void ReplaceCurrent()
    {
        _host.HideNotification();
        _state.SetResult(0, 0);
        var query = NormalizeLineEndingsForDocument(_state.FindText);
        if (string.IsNullOrEmpty(query))
        {
            return;
        }

        if (!FindReplaceService.IsValidQuery(query, _state.Options))
        {
            _host.ShowNotification("Invalid regular expression", _state.IsReplaceMode);
            return;
        }

        var selection = _editor.Selection;
        if (!selection.IsEmpty
            && FindReplaceService.IsMatch(_editor.SelectedText, query, _state.Options))
        {
            var replacement = NormalizeLineEndingsForDocument(_state.ReplaceText);
            var replacedText = FindReplaceService.ReplaceMatch(
                _editor.SelectedText,
                query,
                replacement,
                _state.Options);
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
        _host.HideNotification();
        _state.SetResult(0, 0);
        var query = NormalizeLineEndingsForDocument(_state.FindText);
        if (string.IsNullOrEmpty(query))
        {
            return;
        }

        if (!FindReplaceService.IsValidQuery(query, _state.Options))
        {
            _host.ShowNotification("Invalid regular expression", _state.IsReplaceMode);
            return;
        }

        var count = FindReplaceService.Count(_editor.Text, query, _state.Options);
        if (count > 0)
        {
            _editor.SetText(FindReplaceService.ReplaceAll(
                _editor.Text,
                query,
                NormalizeLineEndingsForDocument(_state.ReplaceText),
                _state.Options));
            _observeText();
            _refreshDocumentView();
        }

        _state.SetResult(0, count);
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
