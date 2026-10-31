using Azunyan.Core;

namespace Azunyan.WinUI;

public sealed partial class AzunyanEditorView
{
    private SnippetSession? _snippetSession;

    /// <summary>
    /// Selects the first tab stop of a snippet just inserted at
    /// <paramref name="offset"/>; Tab and Shift+Tab then move between its stops
    /// until the final one, Esc, or a caret outside the selected stop ends it.
    /// </summary>
    private void StartSnippetSession(int offset, CompletionItem item) =>
        RunAfterComposition(() =>
        {
            if (item.Placeholders.Count > 0)
            {
                SelectSnippetStop(SnippetSession.Start(offset, item.InsertText, item.Placeholders));
            }
        });

    private void MoveSnippetStop(int direction)
    {
        if (_snippetSession is { } session)
        {
            session.Move(direction);
            SelectSnippetStop(session);
        }
    }

    private void SelectSnippetStop(SnippetSession session)
    {
        // Selecting the stop must not end the session it belongs to.
        _snippetSession = null;
        ApplyNavigationCommand(() =>
        {
            _blockSelection = null;
            _document.SetCaretSet(session.GetSelection(position =>
                TextBlockSelectionOperations.GetDisplayColumn(Snapshot, position, TabDisplaySize)));
        });
        _snippetSession = session.IsFinished ? null : session;
        ScrollSelectionIntoView();
    }

    /// <summary>Ends the session, leaving the caret of the first place of its stop.</summary>
    private void EndSnippetSession()
    {
        _snippetSession = null;
        if (_document.CaretSet.Count > 1)
        {
            ApplyNavigationCommand(() => _document.Selection = _document.CaretSet.Primary.Selection);
        }
    }

    private void FollowSnippetEdit(DocumentChangedEventArgs args)
    {
        if (_snippetSession is { } session
            && (args.Kind != DocumentChangeKind.Edit
                || !session.Apply(args.Change, args.OldSnapshot, args.NewSnapshot)))
        {
            _snippetSession = null;
        }
    }

    private void FollowSnippetCarets()
    {
        if (_snippetSession is { } session && !session.Contains(_document.CaretSet))
        {
            _snippetSession = null;
        }
    }
}
