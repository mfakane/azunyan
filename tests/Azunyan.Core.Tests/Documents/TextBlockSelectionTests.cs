using Azunyan.Core;
using Xunit;

namespace Azunyan.Core.Tests.Documents;

public sealed class TextBlockSelectionTests
{
    [Fact]
    public void Selection_normalizes_lines_and_columns_independently()
    {
        var selection = new TextBlockSelection(
            new TextBlockPosition(4, 9),
            new TextBlockPosition(1, 3));

        Assert.Equal(1, selection.TopLine);
        Assert.Equal(4, selection.BottomLine);
        Assert.Equal(3, selection.LeftColumn);
        Assert.Equal(9, selection.RightColumn);
    }

    [Fact]
    public void Line_ranges_stop_at_short_lines()
    {
        var snapshot = new TextSnapshot("012345\n89");
        var selection = new TextBlockSelection(0, 2, 1, 5);

        var ranges = TextBlockSelectionOperations.GetLineRanges(
            snapshot,
            selection,
            tabDisplaySize: 4);

        Assert.Equal(new TextRange(2, 3), ranges[0]);
        Assert.Equal(TextRange.Empty(9), ranges[1]);
    }

    [Fact]
    public void Tabs_use_display_columns_when_mapping_ranges()
    {
        var snapshot = new TextSnapshot("\tabc");

        Assert.Equal(
            new TextRange(0, 1),
            TextBlockSelectionOperations.GetLineRange(
                snapshot,
                line: 0,
                leftColumn: 1,
                rightColumn: 3,
                tabDisplaySize: 4));
        Assert.Equal(
            new TextRange(1, 1),
            TextBlockSelectionOperations.GetLineRange(
                snapshot,
                line: 0,
                leftColumn: 4,
                rightColumn: 5,
                tabDisplaySize: 4));
    }

    [Fact]
    public void Selected_text_preserves_rows_and_uses_the_requested_line_ending()
    {
        var snapshot = new TextSnapshot("abc\ndef\nghi");
        var selection = new TextBlockSelection(0, 1, 1, 3);

        Assert.Equal(
            "bc\r\nef",
            TextBlockSelectionOperations.GetSelectedText(
                snapshot,
                selection,
                tabDisplaySize: 4,
                lineEnding: "\r\n"));
    }

    [Fact]
    public void Single_line_replacement_is_repeated_for_each_selected_row()
    {
        var snapshot = new TextSnapshot("abc\ndef\nghi");
        var selection = new TextBlockSelection(0, 1, 1, 2);

        var edit = TextBlockSelectionOperations.CreateReplacement(
            snapshot,
            selection,
            tabDisplaySize: 4,
            replacementLines: new[] { "X" },
            repeatSingleLine: true,
            lineEnding: "\n");

        Assert.NotNull(edit);
        Assert.Equal(new TextRange(1, 5), edit!.Value.Range);
        Assert.Equal("Xc\ndX", edit.Value.Replacement);
    }

    [Fact]
    public void Multiline_replacement_expands_downward_when_needed()
    {
        var snapshot = new TextSnapshot("a\nb");
        var selection = new TextBlockSelection(0, 0, 0, 1);

        var edit = TextBlockSelectionOperations.CreateReplacement(
            snapshot,
            selection,
            tabDisplaySize: 4,
            replacementLines: new[] { "x", "y", "z" },
            repeatSingleLine: false,
            lineEnding: "\n");

        Assert.NotNull(edit);
        Assert.Equal(new TextRange(0, 3), edit!.Value.Range);
        Assert.Equal("x\ny\nz", edit.Value.Replacement);
    }

    [Fact]
    public void Multiline_replacement_leaves_rows_without_input_unchanged()
    {
        var snapshot = new TextSnapshot("aa\nbb\ncc");
        var selection = new TextBlockSelection(0, 0, 2, 1);

        var edit = TextBlockSelectionOperations.CreateReplacement(
            snapshot,
            selection,
            tabDisplaySize: 4,
            replacementLines: new[] { "x", "y" },
            repeatSingleLine: false,
            lineEnding: "\n");

        Assert.NotNull(edit);
        var result = snapshot.Text[..edit!.Value.Range.Start]
            + edit.Value.Replacement
            + snapshot.Text[edit.Value.Range.End..];
        Assert.Equal("xa\nyb\ncc", result);
    }

    [Fact]
    public void Document_replacement_can_store_the_final_caret_for_undo_and_redo()
    {
        var document = new Document("abc");
        document.SetCaret(1);

        document.Replace(
            new TextRange(0, 1),
            "long",
            TextSelection.Caret(4));

        Assert.Equal("longbc", document.Text);
        Assert.Equal(4, document.CaretPosition);

        Assert.True(document.Undo());
        Assert.Equal("abc", document.Text);
        Assert.Equal(1, document.CaretPosition);

        Assert.True(document.Redo());
        Assert.Equal("longbc", document.Text);
        Assert.Equal(4, document.CaretPosition);
    }
}
