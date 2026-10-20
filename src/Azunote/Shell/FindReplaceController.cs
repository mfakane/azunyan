using Azunyan.Core;

namespace Azunote;

internal enum SearchDirection
{
    Forward,
    Backward
}

/// <summary>
/// Drives the find panel. With In Selection on, every operation is limited to
/// the selection captured when the option was turned on or the panel was
/// opened; the scope follows later edits, and an empty selection leaves the
/// whole document in scope.
/// </summary>
internal sealed class FindReplaceController
{
    private readonly IEditorView _editor;
    private readonly IFindReplaceState _state;
    private readonly IFindReplaceHost _host;
    private readonly Action _refreshDocumentView;
    private readonly Action _observeText;
    private SearchMatch? _scope;

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
        _editor.Edited += (_, change) =>
        {
            if (_scope is { } scope)
            {
                _scope = MapScope(scope, change);
            }
        };
    }

    public void Show(bool replace)
    {
        _state.Show(replace);
        _host.HideNotification();
        _state.SetResult(0, 0);
        _host.FocusFind();
        if (_state.IsInSelection)
        {
            _scope = GetSelectionScope();
            return;
        }

        if (_editor.Selection.Length > 0 && !string.IsNullOrEmpty(_editor.SelectedText))
        {
            _state.FindText = _editor.SelectedText;
            _host.SelectFindText();
        }
    }

    public void ToggleMode() => _state.ToggleMode();

    public void Close()
    {
        _scope = null;
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

    public void OnFindOptionsChanged()
    {
        if (!_state.IsInSelection)
        {
            _scope = null;
        }
        else if (_scope is null)
        {
            _scope = GetSelectionScope();
        }

        OnFindTextChanged();
    }

    public void FindNext()
        => Find(SearchDirection.Forward);

    public void FindPrevious()
        => Find(SearchDirection.Backward);

    private void Find(SearchDirection direction)
    {
        _host.HideNotification();
        _state.SetResult(0, 0);
        if (GetValidQuery() is not { } query)
        {
            return;
        }

        // While the selection is still the captured scope, start from its edge.
        var selection = _editor.Selection;
        var selectionIsScope = _scope == new SearchMatch(selection.Start, selection.Length);
        var result = direction == SearchDirection.Forward
            ? FindReplaceService.FindNextResult(
                _editor.Text,
                query,
                selectionIsScope ? selection.Start : selection.End,
                _state.Options,
                _scope)
            : FindReplaceService.FindPreviousResult(
                _editor.Text,
                query,
                selectionIsScope ? selection.End : selection.Start,
                _state.Options,
                _scope);
        if (result is not { } foundResult)
        {
            return;
        }

        var totalMatches = FindReplaceService.Count(
            _editor.Text,
            query,
            _state.Options,
            _scope);
        var matchNumber = FindReplaceService.CountBefore(
            _editor.Text,
            query,
            foundResult.Match.Start,
            _state.Options,
            _scope) + 1;
        _editor.SetSelection(new TextSelection(
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
        if (GetValidQuery() is not { } query)
        {
            return;
        }

        var selection = _editor.Selection;
        var current = new SearchMatch(selection.Start, selection.Length);
        if (!selection.IsEmpty
            && IsInScope(current)
            && FindReplaceService.IsMatchAt(_editor.Text, query, current, _state.Options))
        {
            var replacedText = FindReplaceService.GetReplacement(
                _editor.Text,
                query,
                NormalizeLineEndingsForDocument(_state.ReplaceText),
                current,
                _state.Options);
            _editor.Replace(selection.Range, replacedText);
            _editor.SetSelection(new TextSelection(
                selection.Start,
                selection.Start + replacedText.Length));
            _observeText();
            _refreshDocumentView();
        }

        Find(SearchDirection.Forward);
    }

    /// <summary>
    /// Replaces every match in scope with one edit, so a single undo reverts it.
    /// </summary>
    public void ReplaceAll()
    {
        _host.HideNotification();
        _state.SetResult(0, 0);
        if (GetValidQuery() is not { } query)
        {
            return;
        }

        var edit = FindReplaceService.ReplaceAll(
            _editor.Text,
            query,
            NormalizeLineEndingsForDocument(_state.ReplaceText),
            _state.Options,
            _scope);
        if (edit is { } replaceAll)
        {
            _editor.Replace(
                new TextRange(replaceAll.Range.Start, replaceAll.Range.Length),
                replaceAll.Replacement);
            _observeText();
            _refreshDocumentView();
        }

        _state.SetResult(0, edit?.Count ?? 0);
    }

    private string? GetValidQuery()
    {
        var query = NormalizeLineEndingsForDocument(_state.FindText);
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        if (!FindReplaceService.IsValidQuery(query, _state.Options))
        {
            _host.ShowNotification("Invalid regular expression", _state.IsReplaceMode);
            return null;
        }

        return query;
    }

    private SearchMatch? GetSelectionScope()
    {
        var selection = _editor.Selection;
        return selection.IsEmpty ? null : new SearchMatch(selection.Start, selection.Length);
    }

    private bool IsInScope(SearchMatch match) =>
        _scope is not { } scope || (match.Start >= scope.Start && match.End <= scope.End);

    /// <summary>
    /// Follows an edit: text inserted at either boundary stays outside, and an
    /// edit that overlaps the scope widens it to cover the new text.
    /// </summary>
    internal static SearchMatch MapScope(SearchMatch scope, TextChange change)
    {
        var delta = change.NewText.Length - change.OldRange.Length;
        if (change.OldRange.End <= scope.Start)
        {
            return scope with { Start = scope.Start + delta };
        }

        if (change.OldRange.Start >= scope.End)
        {
            return scope;
        }

        var start = Math.Min(scope.Start, change.OldRange.Start);
        var end = Math.Max(change.NewRange.End, scope.End + delta);
        return new SearchMatch(start, end - start);
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
