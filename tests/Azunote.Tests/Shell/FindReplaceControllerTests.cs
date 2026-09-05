using Azunote;
using Xunit;

namespace Azunote.Tests.Shell;

public sealed class FindReplaceControllerTests
{
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
        Assert.Equal("Found", view.Result);
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
        Assert.Equal("2 replaced", view.Result);
        Assert.Equal(1, refreshes);
        Assert.Equal(1, observed);
    }
}
