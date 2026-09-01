using Azunyan.Core;
using Xunit;

namespace Azunyan.Core.Tests.Documents;

public sealed class TextCaretSetTests
{
    [Fact]
    public void Block_selection_creates_one_caret_per_line_and_clamps_short_lines()
    {
        var snapshot = new TextSnapshot("012345\nxy\n012345");
        var block = new TextBlockSelection(
            new TextBlockPosition(0, 2),
            new TextBlockPosition(2, 5));

        var carets = TextCaretSetOperations.FromBlockSelection(snapshot, block, 4);

        Assert.Equal(3, carets.Count);
        Assert.Equal(new[] { 5, 9, 15 }, carets.Select(caret => caret.CaretPosition));
        Assert.Equal(
            new[]
            {
                new TextSelection(2, 5),
                TextSelection.Caret(9),
                new TextSelection(12, 15),
            },
            carets.Select(caret => caret.Selection));
        Assert.Equal(2, carets.PrimaryIndex);
    }

    [Fact]
    public void Block_selection_preserves_horizontal_drag_direction_in_each_row()
    {
        var snapshot = new TextSnapshot("abcde\nabcde");
        var block = new TextBlockSelection(
            new TextBlockPosition(1, 4),
            new TextBlockPosition(0, 1));

        var carets = TextCaretSetOperations.FromBlockSelection(snapshot, block, 4);

        Assert.Equal(new TextSelection(4, 1), carets[0].Selection);
        Assert.Equal(new TextSelection(10, 7), carets[1].Selection);
        Assert.Equal(0, carets.PrimaryIndex);
    }

    [Fact]
    public void Visual_block_selection_creates_one_caret_per_wrapped_row()
    {
        var snapshot = new TextSnapshot("abcdef\nXYZ");
        var projection = TextProjectionBuilder.Build(snapshot);
        var rows = VisualRowMapBuilder.Build(projection, wrapColumns: 3);
        var selection = new TextBlockSelection(
            new TextBlockPosition(0, 0),
            new TextBlockPosition(1, 3),
            TextBlockSelectionCoordinateSpace.VisualRows);

        var carets = TextCaretSetOperations.FromVisualBlockSelection(
            snapshot,
            selection,
            rows,
            tabDisplaySize: 4);

        Assert.Equal(2, carets.Count);
        Assert.Equal(
            new[]
            {
                new TextSelection(0, 3),
                new TextSelection(3, 6),
            },
            carets.Select(caret => caret.Selection));
        Assert.Equal(1, carets.PrimaryIndex);
        Assert.Equal("abc\ndef", string.Join(
            "\n",
            carets.Select(caret => snapshot.GetText(caret.Selection.Range))));
    }

    [Fact]
    public void Visual_block_replacement_keeps_adjacent_wrapped_rows_independent()
    {
        var snapshot = new TextSnapshot("abcdef");
        var projection = TextProjectionBuilder.Build(snapshot);
        var rows = VisualRowMapBuilder.Build(projection, wrapColumns: 3);
        var selection = new TextBlockSelection(
            new TextBlockPosition(0, 0),
            new TextBlockPosition(1, 3),
            TextBlockSelectionCoordinateSpace.VisualRows);
        var carets = TextCaretSetOperations.FromVisualBlockSelection(
            snapshot,
            selection,
            rows,
            tabDisplaySize: 4);

        var edit = TextCaretSetOperations.CreateReplacement(
            snapshot,
            carets,
            new string?[] { "X", "Y" },
            tabDisplaySize: 4);

        var result = snapshot.Text[..edit.Range.Start]
            + edit.Replacement
            + snapshot.Text[edit.Range.End..];
        Assert.Equal("XY", result);
    }

    [Fact]
    public void Visual_block_paste_maps_clipboard_rows_and_appends_extra_rows()
    {
        var snapshot = new TextSnapshot("abcdef");
        var projection = TextProjectionBuilder.Build(snapshot);
        var rows = VisualRowMapBuilder.Build(projection, wrapColumns: 3);
        var selection = new TextBlockSelection(
            new TextBlockPosition(0, 1),
            new TextBlockPosition(1, 2),
            TextBlockSelectionCoordinateSpace.VisualRows);
        var carets = TextCaretSetOperations.FromVisualBlockSelection(
            snapshot,
            selection,
            rows,
            tabDisplaySize: 4);

        var edit = TextCaretSetOperations.CreatePaste(
            snapshot,
            carets,
            new[] { "X", "Y", "Z" },
            "\n",
            tabDisplaySize: 4);

        var result = snapshot.Text[..edit.Range.Start]
            + edit.Replacement
            + snapshot.Text[edit.Range.End..];
        Assert.Equal("aXcdYf\nZ", result);
    }

    [Fact]
    public void Movement_keeps_each_selection_anchor_when_shift_is_pressed()
    {
        var snapshot = new TextSnapshot("abc\ndef");
        var carets = new TextCaretSet(new[]
        {
            new TextCaretState(new TextSelection(1, 1), 1),
            new TextCaretState(new TextSelection(5, 5), 1),
        }, primaryIndex: 1);

        var moved = TextCaretSetOperations.MoveHorizontal(snapshot, carets, 1, extendSelection: true);

        Assert.Equal(new TextSelection(1, 2), moved[0].Selection);
        Assert.Equal(new TextSelection(5, 6), moved[1].Selection);
        Assert.Equal(1, moved.PrimaryIndex);
    }

    [Fact]
    public void Matching_bracket_movement_supports_multiple_carets_and_selection_extension()
    {
        var snapshot = new TextSnapshot("(a) [b]");
        var carets = new TextCaretSet(new[]
        {
            new TextCaretState(TextSelection.Caret(0), 0),
            new TextCaretState(TextSelection.Caret(snapshot.Length), 7),
        }, primaryIndex: 1);

        var moved = TextCaretSetOperations.MoveToMatchingBracket(
            snapshot,
            carets,
            extendSelection: true,
            tabDisplaySize: 4);

        Assert.Equal(new TextSelection(0, 2), moved[0].Selection);
        Assert.Equal(new TextSelection(snapshot.Length, 4), moved[1].Selection);
        Assert.Equal(1, moved.PrimaryIndex);
    }

    [Fact]
    public void Replacement_is_one_edit_and_undo_restores_the_full_caret_set()
    {
        var document = new Document("aa\nbb\ncc");
        var carets = new TextCaretSet(new[]
        {
            new TextCaretState(TextSelection.Caret(1), 1),
            new TextCaretState(TextSelection.Caret(4), 1),
            new TextCaretState(TextSelection.Caret(7), 1),
        }, primaryIndex: 1);
        document.SetCaretSet(carets);

        var edit = TextCaretSetOperations.CreateReplacement(
            document.Snapshot,
            document.CaretSet,
            new string?[] { "X", "Y", "Z" },
            tabDisplaySize: 4);
        document.Replace(edit.Range, edit.Replacement, edit.CaretSet);

        Assert.Equal("aXa\nbYb\ncZc", document.Text);
        Assert.Equal(new[] { 2, 6, 10 }, document.CaretSet.Select(caret => caret.CaretPosition));
        Assert.Equal(1, document.CaretSet.PrimaryIndex);

        Assert.True(document.Undo());
        Assert.Equal("aa\nbb\ncc", document.Text);
        Assert.Equal(carets, document.CaretSet);
        Assert.True(document.Redo());
        Assert.Equal("aXa\nbYb\ncZc", document.Text);
        Assert.Equal(new[] { 2, 6, 10 }, document.CaretSet.Select(caret => caret.CaretPosition));
    }

    [Fact]
    public void Deletion_merges_overlapping_ranges_and_removes_crlf_as_one_break()
    {
        var snapshot = new TextSnapshot("ab\r\ncd");
        var carets = new TextCaretSet(new[]
        {
            new TextCaretState(TextSelection.Caret(4), 0),
            new TextCaretState(TextSelection.Caret(3), 0),
        });

        var edit = TextCaretSetOperations.CreateDeletion(snapshot, carets, backward: true, 4);
        var result = snapshot.Text[..edit.Range.Start]
            + edit.Replacement
            + snapshot.Text[edit.Range.End..];

        Assert.Equal("abcd", result);
        Assert.Single(edit.CaretSet);
    }

    [Fact]
    public void Vertical_movement_accepts_page_sized_line_deltas()
    {
        var snapshot = new TextSnapshot("zero\none\ntwo\nthree");
        var carets = new TextCaretSet(new[]
        {
            new TextCaretState(TextSelection.Caret(snapshot.Text.IndexOf("three", StringComparison.Ordinal) + 2), 2),
        });

        var moved = TextCaretSetOperations.MoveVertical(
            snapshot,
            carets,
            direction: -2,
            tabDisplaySize: 4,
            extendSelection: false);

        Assert.Equal(snapshot.Text.IndexOf("one", StringComparison.Ordinal) + 2, moved.Primary.CaretPosition);
        Assert.Equal(2, moved.Primary.PreferredDisplayColumn);
    }

    [Fact]
    public void Smart_home_toggles_between_indentation_and_logical_line_start()
    {
        var snapshot = new TextSnapshot("    value");
        var carets = new TextCaretSet(new[]
        {
            new TextCaretState(TextSelection.Caret(snapshot.Length), snapshot.Length),
        });

        var indentation = TextCaretSetOperations.MoveToSmartLineStart(
            snapshot,
            carets,
            extendSelection: false,
            tabDisplaySize: 4);
        var lineStart = TextCaretSetOperations.MoveToSmartLineStart(
            snapshot,
            indentation,
            extendSelection: false,
            tabDisplaySize: 4);

        Assert.Equal(4, indentation.Primary.CaretPosition);
        Assert.Equal(0, lineStart.Primary.CaretPosition);

        var whitespace = new TextSnapshot("    ");
        var whitespaceHome = TextCaretSetOperations.MoveToSmartLineStart(
            whitespace,
            new TextCaretSet(new[]
            {
                new TextCaretState(TextSelection.Caret(2), 2),
            }),
            extendSelection: false,
            tabDisplaySize: 4);
        Assert.Equal(0, whitespaceHome.Primary.CaretPosition);
    }

    [Fact]
    public void Word_deletion_and_auto_indented_newlines_apply_to_every_caret()
    {
        var deletionSnapshot = new TextSnapshot("one two");
        var deletionCarets = new TextCaretSet(new[]
        {
            new TextCaretState(TextSelection.Caret(deletionSnapshot.Length), 7),
        });
        var deletion = TextCaretSetOperations.CreateDeletion(
            deletionSnapshot,
            deletionCarets,
            backward: true,
            tabDisplaySize: 4,
            byWord: true);
        var deleted = deletionSnapshot.Text[..deletion.Range.Start]
            + deletion.Replacement
            + deletionSnapshot.Text[deletion.Range.End..];

        var newlineSnapshot = new TextSnapshot("  one\n\tsecond");
        var newlineCarets = new TextCaretSet(new[]
        {
            new TextCaretState(TextSelection.Caret(5), 5),
            new TextCaretState(TextSelection.Caret(newlineSnapshot.Length), 8),
        });
        var newline = TextCaretSetOperations.CreateNewLineWithAutoIndent(
            newlineSnapshot,
            newlineCarets,
            preferredLineEnding: "\n",
            tabDisplaySize: 4);
        var inserted = newlineSnapshot.Text[..newline.Range.Start]
            + newline.Replacement
            + newlineSnapshot.Text[newline.Range.End..];

        Assert.Equal("one ", deleted);
        Assert.Equal("  one\n  \n\tsecond\n\t", inserted);
        Assert.Equal(2, newline.CaretSet.Count);
    }

    [Fact]
    public void Multiline_paste_maps_rows_and_appends_extra_rows()
    {
        var snapshot = new TextSnapshot("aa\nbb");
        var carets = new TextCaretSet(new[]
        {
            new TextCaretState(TextSelection.Caret(1), 1),
            new TextCaretState(TextSelection.Caret(4), 1),
        });

        var edit = TextCaretSetOperations.CreatePaste(
            snapshot,
            carets,
            new[] { "X", "Y", "Z" },
            "\n",
            4);
        var result = snapshot.Text[..edit.Range.Start]
            + edit.Replacement
            + snapshot.Text[edit.Range.End..];

        Assert.Equal("aXa\nbYb\nZ", result);
    }
}
