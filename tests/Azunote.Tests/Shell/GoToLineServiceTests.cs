using Azunote;
using Azunyan.Core;
using Xunit;

namespace Azunote.Tests.Shell;

public sealed class GoToLineServiceTests
{
    [Theory]
    [InlineData("12", 12, null)]
    [InlineData("12:4", 12, 4)]
    [InlineData(" 12:4 ", 12, 4)]
    public void Try_parse_accepts_line_and_line_column_forms(
        string input,
        int expectedLine,
        int? expectedColumn)
    {
        Assert.True(GoToLineService.TryParse(input, out var target));
        Assert.Equal(new GoToLineTarget(expectedLine, expectedColumn), target);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0")]
    [InlineData("12:0")]
    [InlineData("-1")]
    [InlineData("12:-4")]
    [InlineData("12 : 4")]
    [InlineData("12:4:8")]
    [InlineData("line")]
    [InlineData("999999999999999999999")]
    public void Try_parse_rejects_invalid_input(string input)
    {
        Assert.False(GoToLineService.TryParse(input, out _));
    }

    [Fact]
    public void Resolve_uses_column_one_when_only_a_line_is_given()
    {
        var snapshot = new TextSnapshot("first\nsecond");

        var position = GoToLineService.Resolve(
            snapshot,
            new GoToLineTarget(2));

        Assert.Equal(new LineColumn(1, 0), position);
    }

    [Fact]
    public void Resolve_clamps_line_and_column_to_the_document()
    {
        var snapshot = new TextSnapshot("first\nsecond");

        var position = GoToLineService.Resolve(
            snapshot,
            new GoToLineTarget(int.MaxValue, int.MaxValue));

        Assert.Equal(new LineColumn(1, 6), position);
    }
}

public sealed class GoToLineControllerTests
{
    [Fact]
    public async Task Show_uses_the_current_position_and_moves_the_editor()
    {
        var editor = new FakeEditorView("first\nsecond\nthird");
        editor.SetSelection(TextSelection.Caret(7));
        var dialog = new FakeGoToLineDialog
        {
            Result = new GoToLineTarget(3, 2)
        };
        var controller = new GoToLineController(editor, dialog);

        await controller.ShowAsync();

        Assert.Equal("2:2", dialog.InitialText);
        Assert.Equal(
            TextSelection.Caret(editor.Snapshot.Lines.GetPosition(new LineColumn(2, 1))),
            editor.Selection);
        Assert.Equal(1, editor.FocusCount);
    }

    [Fact]
    public async Task Show_leaves_the_editor_unchanged_when_cancelled()
    {
        var editor = new FakeEditorView("first\nsecond");
        editor.SetSelection(TextSelection.Caret(3));
        var originalSelection = editor.Selection;
        var controller = new GoToLineController(editor, new FakeGoToLineDialog());

        await controller.ShowAsync();

        Assert.Equal(originalSelection, editor.Selection);
        Assert.Equal(0, editor.FocusCount);
    }

    private sealed class FakeGoToLineDialog : IGoToLineDialog
    {
        public string? InitialText { get; private set; }

        public GoToLineTarget? Result { get; init; }

        public Task<GoToLineTarget?> ShowAsync(string initialText)
        {
            InitialText = initialText;
            return Task.FromResult(Result);
        }
    }
}
