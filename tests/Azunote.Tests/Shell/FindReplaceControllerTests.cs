using Azunote;
using Xunit;

namespace Azunote.Tests.Shell;

public sealed class FindReplaceControllerTests
{
    [Fact]
    public void Show_selects_the_requested_find_replace_mode()
    {
        var view = new FakeFindReplaceView();
        var controller = new FindReplaceController(
            new FakeEditorView(),
            view,
            view,
            () => { },
            () => { });

        controller.Show(replace: true);
        Assert.True(view.IsReplaceMode);

        controller.Show(replace: false);
        Assert.False(view.IsReplaceMode);

        controller.ToggleMode();
        Assert.True(view.IsReplaceMode);

        controller.ToggleMode();
        Assert.False(view.IsReplaceMode);
    }

    [Fact]
    public void Find_next_and_replace_current_preserve_editor_selection_flow()
    {
        var editor = new FakeEditorView("one two one");
        var view = new FakeFindReplaceView
        {
            FindText = "one",
            ReplaceText = "uno"
        };
        var observed = 0;
        var controller = new FindReplaceController(
            editor,
            view,
            view,
            () => { },
            () => observed++);

        controller.FindNext();
        Assert.Equal(new Azunyan.Core.TextSelection(0, 3), editor.Selection);
        controller.ReplaceCurrent();

        Assert.Equal("uno two one", editor.Text);
        Assert.Equal(1, observed);
        Assert.Equal("1/1", view.Result);
    }

    [Fact]
    public void Find_reports_match_position_and_notifies_when_search_wraps()
    {
        var editor = new FakeEditorView("one two one");
        var view = new FakeFindReplaceView
        {
            FindText = "one",
            IsFindBoxFocused = true
        };
        var controller = new FindReplaceController(
            editor,
            view,
            view,
            () => { },
            () => { });

        controller.FindNext();
        Assert.Equal("1/2", view.Result);
        Assert.Null(view.Notification);
        Assert.Equal(1, editor.ScrollSelectionIntoViewCount);

        controller.FindNext();
        Assert.Equal("2/2", view.Result);
        Assert.Null(view.Notification);

        controller.FindNext();
        Assert.Equal("1/2", view.Result);
        Assert.Equal("Search wrapped to the beginning", view.Notification);
    }

    [Fact]
    public void Find_does_not_take_focus_from_the_find_replace_input()
    {
        var editor = new FakeEditorView("one two one");
        var view = new FakeFindReplaceView
        {
            FindText = "one",
            IsFindBoxFocused = true
        };
        var controller = new FindReplaceController(
            editor,
            view,
            view,
            () => { },
            () => { });

        controller.FindNext();

        Assert.Equal(0, editor.FocusCount);
        Assert.True(view.IsFindBoxFocused);
    }

    [Fact]
    public void Invalid_regular_expression_is_shown_in_a_notification()
    {
        var view = new FakeFindReplaceView
        {
            FindText = "(",
            Options = FindReplaceOptions.RegularExpression
        };
        var controller = new FindReplaceController(
            new FakeEditorView("text"),
            view,
            view,
            () => { },
            () => { });

        controller.FindNext();

        Assert.Equal("0/0", view.Result);
        Assert.Equal("Invalid regular expression", view.Notification);
    }

    [Fact]
    public void Not_found_reports_zero_matches_and_marks_the_find_text_error()
    {
        var view = new FakeFindReplaceView
        {
            FindText = "missing",
            IsFindBoxFocused = true
        };
        var controller = new FindReplaceController(
            new FakeEditorView("text"),
            view,
            view,
            () => { },
            () => { });

        controller.FindNext();

        Assert.Equal("0/0", view.Result);
    }

    [Fact]
    public void Find_uses_the_options_exposed_by_the_view()
    {
        var editor = new FakeEditorView("Foo foo foobar");
        var view = new FakeFindReplaceView
        {
            FindText = "foo",
            Options = FindReplaceOptions.MatchCase | FindReplaceOptions.MatchWholeWord
        };
        var controller = new FindReplaceController(
            editor,
            view,
            view,
            () => { },
            () => { });

        controller.FindNext();

        Assert.Equal(new Azunyan.Core.TextSelection(4, 7), editor.Selection);
    }

    [Fact]
    public void Replace_all_reports_count_and_observes_the_new_text()
    {
        var editor = new FakeEditorView("Foo foo");
        var view = new FakeFindReplaceView
        {
            FindText = "foo",
            ReplaceText = "bar"
        };
        var refreshes = 0;
        var observed = 0;
        var controller = new FindReplaceController(
            editor,
            view,
            view,
            () => refreshes++,
            () => observed++);

        controller.ReplaceAll();

        Assert.Equal("bar bar", editor.Text);
        Assert.Equal("0/2", view.Result);
        Assert.Equal(1, refreshes);
        Assert.Equal(1, observed);
    }

    [Fact]
    public void Replace_all_is_one_undoable_edit()
    {
        var editor = new FakeEditorView("a foo b foo c");
        var view = new FakeFindReplaceView { FindText = "foo", ReplaceText = "bar" };
        var controller = new FindReplaceController(editor, view, view, () => { }, () => { });

        controller.ReplaceAll();
        Assert.Equal("a bar b bar c", editor.Text);

        editor.Undo();
        Assert.Equal("a foo b foo c", editor.Text);
    }

    [Fact]
    public void In_selection_limits_find_and_replace_to_the_captured_selection_as_it_is_edited()
    {
        var editor = new FakeEditorView("x x x x");
        editor.SetSelection(new Azunyan.Core.TextSelection(2, 5));
        var view = new FakeFindReplaceView { FindText = "x", ReplaceText = "yy", IsInSelection = true };
        var controller = new FindReplaceController(editor, view, view, () => { }, () => { });
        controller.Show(replace: true);

        controller.FindNext();
        Assert.Equal(new Azunyan.Core.TextSelection(2, 3), editor.Selection);
        Assert.Equal("1/2", view.Result);
        Assert.Null(view.Notification);

        controller.FindNext();
        Assert.Equal(new Azunyan.Core.TextSelection(4, 5), editor.Selection);
        Assert.Equal("2/2", view.Result);

        controller.ReplaceCurrent();
        Assert.Equal("x x yy x", editor.Text);
        Assert.Equal(new Azunyan.Core.TextSelection(2, 3), editor.Selection);

        controller.ReplaceAll();
        Assert.Equal("x yy yy x", editor.Text);
        Assert.Equal("0/1", view.Result);
    }

    [Fact]
    public void Replace_current_expands_regex_substitutions_with_lookbehind_context()
    {
        var editor = new FakeEditorView("ab cb");
        var view = new FakeFindReplaceView
        {
            FindText = "(?<=a)(b)",
            ReplaceText = "[$1]",
            Options = FindReplaceOptions.RegularExpression
        };
        var controller = new FindReplaceController(editor, view, view, () => { }, () => { });

        controller.FindNext();
        controller.ReplaceCurrent();

        Assert.Equal("a[b] cb", editor.Text);
    }

    [Fact]
    public void Scope_shifts_before_it_grows_inside_it_and_ignores_edits_after_it()
    {
        var scope = new SearchMatch(4, 4);

        Assert.Equal(
            new SearchMatch(6, 4),
            FindReplaceController.MapScope(scope, new Azunyan.Core.TextChange(Azunyan.Core.TextRange.Empty(4), "", "ab")));
        Assert.Equal(
            new SearchMatch(4, 6),
            FindReplaceController.MapScope(scope, new Azunyan.Core.TextChange(new Azunyan.Core.TextRange(5, 1), "x", "yyy")));
        Assert.Equal(
            scope,
            FindReplaceController.MapScope(scope, new Azunyan.Core.TextChange(Azunyan.Core.TextRange.Empty(8), "", "z")));
    }

    [Fact]
    public void Find_previous_selects_the_match_before_the_current_selection()
    {
        var editor = new FakeEditorView("one two one");
        var view = new FakeFindReplaceView
        {
            FindText = "one"
        };
        var controller = new FindReplaceController(
            editor,
            view,
            view,
            () => { },
            () => { });

        controller.FindNext();
        controller.FindNext();
        controller.FindPrevious();

        Assert.Equal(new Azunyan.Core.TextSelection(0, 3), editor.Selection);
        Assert.Equal("1/2", view.Result);
    }
}
