using Azunyan.Core;
using Xunit;

namespace Azunyan.Core.Tests;

public sealed class DocumentTests
{
    [Fact]
    public void Edits_return_coarse_changes_and_keep_snapshots_immutable()
    {
        var document = new Document("0123456789");
        var original = document.Snapshot;

        var change = document.Replace(new TextRange(2, 3), "abc");

        Assert.Equal("0123456789", original.Text);
        Assert.Equal("01abc56789", document.Text);
        Assert.Equal(new TextRange(2, 3), change.OldRange);
        Assert.Equal("234", change.OldText);
        Assert.Equal("abc", change.NewText);
        Assert.Equal(new TextRange(2, 3), change.NewRange);
        Assert.Equal("23456", original.GetText(new TextRange(2, 5)));
        Assert.Equal("abc56", document.Snapshot.GetText(new TextRange(2, 5)));
    }

    [Fact]
    public void Undo_and_redo_restore_text_and_selection()
    {
        var document = new Document("hello");
        document.SetSelection(new TextSelection(1, 4));

        document.Replace("i");

        Assert.Equal("hio", document.Text);
        Assert.Equal(TextSelection.Caret(2), document.Selection);
        Assert.True(document.Undo());
        Assert.Equal("hello", document.Text);
        Assert.Equal(new TextSelection(1, 4), document.Selection);
        Assert.True(document.Redo());
        Assert.Equal("hio", document.Text);
        Assert.Equal(TextSelection.Caret(2), document.Selection);
    }

    [Fact]
    public void New_edit_clears_redo_history()
    {
        var document = new Document("a");
        document.Insert(1, "b");
        Assert.True(document.Undo());

        document.Insert(0, "c");

        Assert.False(document.CanRedo);
        Assert.Equal("ca", document.Text);
    }

    [Fact]
    public void Line_index_handles_all_newline_forms()
    {
        var snapshot = new TextSnapshot("a\r\nb\nc\rd");
        var lines = snapshot.Lines;

        Assert.Equal(4, lines.LineCount);
        Assert.Equal(new TextRange(0, 1), lines.GetLineRange(0));
        Assert.Equal(new TextRange(3, 1), lines.GetLineRange(1));
        Assert.Equal(new TextRange(5, 1), lines.GetLineRange(2));
        Assert.Equal(new TextRange(7, 1), lines.GetLineRange(3));
        Assert.Equal(new LineColumn(2, 1), lines.GetLineColumn(6));
        Assert.Equal(6, lines.GetPosition(new LineColumn(2, 1)));
    }

    [Fact]
    public void Search_supports_non_overlapping_and_overlapping_matches()
    {
        var snapshot = new TextSnapshot("ababa");

        Assert.Equal(new TextRange(0, 3), snapshot.Find("aba"));
        Assert.Equal(
            new[] { new TextRange(0, 3) },
            snapshot.FindAll("aba"));
        Assert.Equal(
            new[] { new TextRange(0, 3), new TextRange(2, 3) },
            snapshot.FindAll("aba", allowOverlapping: true));
    }

    [Fact]
    public void Persistent_text_tree_survives_many_random_edits()
    {
        var random = new Random(42);
        var expected = string.Empty;
        var document = new Document();

        for (var iteration = 0; iteration < 1000; iteration++)
        {
            var start = random.Next(expected.Length + 1);
            var length = random.Next(expected.Length - start + 1);
            var inserted = new string((char)('a' + random.Next(3)), random.Next(4));

            document.Replace(new TextRange(start, length), inserted);
            expected = expected.Remove(start, length).Insert(start, inserted);

            Assert.Equal(expected, document.Text);

            var rangeStart = random.Next(expected.Length + 1);
            var rangeLength = random.Next(expected.Length - rangeStart + 1);
            Assert.Equal(
                expected.Substring(rangeStart, rangeLength),
                document.Snapshot.GetText(new TextRange(rangeStart, rangeLength)));
        }
    }

    [Fact]
    public void Scalar_navigation_does_not_split_surrogate_pairs()
    {
        const string text = "A😀B";

        Assert.Equal(3, UnicodeText.GetNextScalarPosition(text, 1));
        Assert.Equal(1, UnicodeText.GetPreviousScalarPosition(text, 3));
        Assert.Equal(3, UnicodeText.MoveByScalars(text, 0, 2));

        var document = new Document(text);
        document.SetCaret(3);
        document.DeleteBackwardByScalar();
        Assert.Equal("AB", document.Text);
    }

    [Fact]
    public void Grapheme_navigation_and_delete_keep_emoji_and_combining_sequences_together()
    {
        const string text = "a👩‍💻éb";
        var emojiStart = 1;
        var emojiEnd = UnicodeText.GetNextTextElementPosition(text, emojiStart);
        var combiningStart = emojiEnd;
        var combiningEnd = UnicodeText.GetNextTextElementPosition(text, combiningStart);

        Assert.Equal("👩‍💻", text[emojiStart..emojiEnd]);
        Assert.Equal("é", text[combiningStart..combiningEnd]);

        var document = new Document(text);
        document.SetCaret(emojiEnd);
        document.DeleteBackward();
        Assert.Equal("aéb", document.Text);

        document.SetCaret(1);
        document.DeleteForward();
        Assert.Equal("ab", document.Text);
    }

    [Fact]
    public void Grapheme_movement_preserves_selection_anchor()
    {
        var document = new Document("a👩‍💻b");
        document.SetCaret(document.Length);
        document.MoveCaretByGrapheme(-2, extendSelection: true);

        Assert.Equal(new TextSelection(document.Length, 1), document.Selection);
    }
}
