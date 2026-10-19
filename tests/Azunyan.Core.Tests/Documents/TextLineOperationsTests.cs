using Azunyan.Core;
using Xunit;

namespace Azunyan.Core.Tests.Documents;

public sealed class TextLineOperationsTests
{
    [Fact]
    public void Move_down_swaps_the_caret_line_with_the_next_line_and_keeps_the_column()
    {
        var snapshot = new TextSnapshot("one\ntwo\nthree");

        var edit = TextLineOperations.MoveLines(snapshot, Carets(TextSelection.Caret(1)), down: true);

        Assert.Equal("two\none\nthree", Apply(snapshot, edit));
        Assert.Equal(TextSelection.Caret(5), edit!.Value.CaretSet.Primary.Selection);
    }

    [Fact]
    public void Move_up_keeps_line_endings_in_place_when_the_last_line_moves()
    {
        var snapshot = new TextSnapshot("one\r\ntwo\nthree");

        var edit = TextLineOperations.MoveLines(snapshot, Carets(TextSelection.Caret(12)), down: false);

        Assert.Equal("one\r\nthree\ntwo", Apply(snapshot, edit));
        Assert.Equal(TextSelection.Caret(8), edit!.Value.CaretSet.Primary.Selection);
    }

    [Fact]
    public void Move_excludes_a_line_where_the_selection_ends_at_column_zero()
    {
        var snapshot = new TextSnapshot("a\nb\nc\nd");

        var edit = TextLineOperations.MoveLines(snapshot, Carets(new TextSelection(2, 6)), down: false);

        Assert.Equal("b\nc\na\nd", Apply(snapshot, edit));
        Assert.Equal(new TextSelection(0, 4), edit!.Value.CaretSet.Primary.Selection);
    }

    [Fact]
    public void Move_does_nothing_when_a_block_is_at_the_document_boundary()
    {
        var snapshot = new TextSnapshot("a\nb\nc");
        var carets = Carets(TextSelection.Caret(0), TextSelection.Caret(4));

        Assert.Null(TextLineOperations.MoveLines(snapshot, carets, down: false));
        Assert.Null(TextLineOperations.MoveLines(snapshot, Carets(TextSelection.Caret(4)), down: true));
    }

    [Fact]
    public void Move_moves_separate_and_adjacent_caret_lines_together()
    {
        var snapshot = new TextSnapshot("a\nb\nc\nd\ne");
        var carets = Carets(TextSelection.Caret(0), TextSelection.Caret(2), TextSelection.Caret(6));

        var edit = TextLineOperations.MoveLines(snapshot, carets, down: true);

        Assert.Equal("c\na\nb\ne\nd", Apply(snapshot, edit));
        Assert.Equal(new[] { 2, 4, 8 }, edit!.Value.CaretSet.Select(caret => caret.CaretPosition));
    }

    [Fact]
    public void Duplicate_down_moves_the_selection_to_the_lower_copy()
    {
        var snapshot = new TextSnapshot("a\nbc\nd");

        var edit = TextLineOperations.DuplicateLines(snapshot, Carets(new TextSelection(3, 4)), down: true, "\n");

        Assert.Equal("a\nbc\nbc\nd", Apply(snapshot, edit));
        Assert.Equal(new TextSelection(6, 7), edit.CaretSet.Primary.Selection);
    }

    [Fact]
    public void Duplicate_up_keeps_the_selection_on_the_upper_copy()
    {
        var snapshot = new TextSnapshot("a\nb\nc");

        var edit = TextLineOperations.DuplicateLines(snapshot, Carets(new TextSelection(0, 4)), down: false, null);

        Assert.Equal("a\nb\na\nb\nc", Apply(snapshot, edit));
        Assert.Equal(new TextSelection(0, 4), edit.CaretSet.Primary.Selection);
    }

    [Fact]
    public void Duplicate_of_the_last_line_uses_the_preferred_line_ending()
    {
        var snapshot = new TextSnapshot("a\r\nb");

        var edit = TextLineOperations.DuplicateLines(snapshot, Carets(TextSelection.Caret(4)), down: true, "\r\n");

        Assert.Equal("a\r\nb\r\nb", Apply(snapshot, edit));
        Assert.Equal(TextSelection.Caret(7), edit.CaretSet.Primary.Selection);
    }

    [Fact]
    public void Delete_removes_the_line_and_keeps_the_column_on_the_next_line()
    {
        var snapshot = new TextSnapshot("one\ntwo\nx");

        var edit = TextLineOperations.DeleteLines(snapshot, Carets(TextSelection.Caret(2)));

        Assert.Equal("two\nx", Apply(snapshot, edit));
        Assert.Equal(TextSelection.Caret(2), edit.CaretSet.Primary.Selection);
    }

    [Fact]
    public void Delete_of_the_last_line_removes_the_preceding_line_ending()
    {
        var snapshot = new TextSnapshot("one\ntwo");

        var edit = TextLineOperations.DeleteLines(snapshot, Carets(new TextSelection(5, 6)));

        Assert.Equal("one", Apply(snapshot, edit));
        Assert.Equal(TextSelection.Caret(1), edit.CaretSet.Primary.Selection);
    }

    [Fact]
    public void Delete_of_every_line_leaves_an_empty_document_and_merges_carets()
    {
        var snapshot = new TextSnapshot("a\nb");

        var edit = TextLineOperations.DeleteLines(
            snapshot,
            Carets(TextSelection.Caret(0), TextSelection.Caret(3)));

        Assert.Equal(string.Empty, Apply(snapshot, edit));
        Assert.Equal(TextSelection.Caret(0), Assert.Single(edit.CaretSet).Selection);
    }

    [Fact]
    public void Line_edit_applies_as_one_undo_record()
    {
        var document = new Document("a\nb");
        document.SetCaretSet(Carets(TextSelection.Caret(0)));
        var edit = TextLineOperations.MoveLines(document.Snapshot, document.CaretSet, down: true)!.Value;

        document.Replace(edit.Range, edit.Replacement, edit.CaretSet);
        Assert.Equal("b\na", document.Text);
        Assert.Equal(2, document.CaretPosition);

        Assert.True(document.Undo());
        Assert.Equal("a\nb", document.Text);
    }

    [Fact]
    public void Line_comment_is_inserted_at_the_smallest_indentation_and_skips_blank_lines()
    {
        var snapshot = new TextSnapshot("    a\n\n  b\nc");

        var edit = TextLineOperations.ToggleLineComment(snapshot, Carets(new TextSelection(0, 10)), "//");

        Assert.Equal("  //   a\n\n  // b\nc", Apply(snapshot, edit));
        Assert.Equal(new TextSelection(0, 16), edit!.Value.CaretSet.Primary.Selection);
    }

    [Fact]
    public void Line_comment_moves_an_empty_caret_past_the_inserted_token()
    {
        var snapshot = new TextSnapshot("value");

        var edit = TextLineOperations.ToggleLineComment(snapshot, Carets(TextSelection.Caret(0)), "#");

        Assert.Equal("# value", Apply(snapshot, edit));
        Assert.Equal(TextSelection.Caret(2), edit!.Value.CaretSet.Primary.Selection);
    }

    [Fact]
    public void Line_comment_is_removed_when_every_non_blank_line_is_commented()
    {
        var snapshot = new TextSnapshot("  // a\n//b\n\nc");

        var edit = TextLineOperations.ToggleLineComment(snapshot, Carets(new TextSelection(4, 10)), "//");

        Assert.Equal("  a\nb\n\nc", Apply(snapshot, edit));
        Assert.Equal(new TextSelection(2, 5), edit!.Value.CaretSet.Primary.Selection);
    }

    [Fact]
    public void Line_comment_is_added_when_only_some_lines_are_commented()
    {
        var snapshot = new TextSnapshot("// a\nb");

        var edit = TextLineOperations.ToggleLineComment(snapshot, Carets(new TextSelection(0, 6)), "//");

        Assert.Equal("// // a\n// b", Apply(snapshot, edit));
    }

    [Fact]
    public void Line_comment_does_nothing_on_blank_lines()
    {
        Assert.Null(TextLineOperations.ToggleLineComment(
            new TextSnapshot("  \n"),
            Carets(TextSelection.Caret(0)),
            "//"));
    }

    [Fact]
    public void Block_comment_wraps_and_unwraps_the_text_of_the_selected_lines()
    {
        var snapshot = new TextSnapshot("  <a>\n  </a>\nz");

        var wrapped = TextLineOperations.ToggleBlockComment(
            snapshot,
            Carets(new TextSelection(3, 9)),
            "<!--",
            "-->");

        var wrappedText = Apply(snapshot, wrapped);
        Assert.Equal("  <!-- <a>\n  </a> -->\nz", wrappedText);

        var commented = new TextSnapshot(wrappedText);
        var unwrapped = TextLineOperations.ToggleBlockComment(
            commented,
            Carets(new TextSelection(0, 21)),
            "<!--",
            "-->");
        Assert.Equal("  <a>\n  </a>\nz", Apply(commented, unwrapped));
    }

    private static TextCaretSet Carets(params TextSelection[] selections) =>
        new(selections.Select(selection => new TextCaretState(selection, 0)));

    private static string Apply(TextSnapshot snapshot, TextCaretEdit? edit)
    {
        Assert.NotNull(edit);
        var text = snapshot.Text;
        return text[..edit.Value.Range.Start]
            + edit.Value.Replacement
            + text[edit.Value.Range.End..];
    }
}
