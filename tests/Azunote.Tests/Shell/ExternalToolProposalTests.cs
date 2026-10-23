using Azunote;
using Azunyan.Core;
using Xunit;

namespace Azunote.Tests.Shell;

public sealed class ExternalToolProposalTests
{
    // Lines a to j at offsets 0, 2, 4, ...; the tool changes b to B.
    private const string Text = "a\nb\nc\nd\ne\nf\ng\nh\ni\nj\n";

    // The diff's lines start at 0, 14, 28 (the hunk header), 44 " a", 47 "-b",
    // 50 "+B", 53 " c", 56 " d", 59 " e".
    private static ExternalToolProposal Propose(FakeEditorView editor) =>
        new(
            editor,
            new TrackedTextRange(editor, new TextRange(0, Text.Length), wholeDocument: true),
            Text,
            Text.Replace("b\n", "B\n", StringComparison.Ordinal),
            "Tool",
            "doc.txt",
            diagnostics: null,
            observeText: () => { });

    [Fact]
    public void The_diff_is_laid_out_as_the_offsets_here_assume()
    {
        var proposal = Propose(new FakeEditorView(Text));

        Assert.Equal("--- a/doc.txt\n+++ b/doc.txt\n@@ -1,5 +1,5 @@\n a\n-b\n+B\n c\n d\n e\n", proposal.Diff);
    }

    [Theory]
    // A caret on a shown line is on that line of the diff.
    [InlineData(2, 2, 48, 48)]
    // A selection takes the part of it the diff shows.
    [InlineData(6, 12, 57, 61)]
    public void A_document_selection_maps_to_the_diff(int anchor, int active, int diffAnchor, int diffActive)
    {
        var proposal = Propose(new FakeEditorView(Text));

        Assert.Equal(new TextSelection(diffAnchor, diffActive), proposal.ToDiff(new TextSelection(anchor, active)));
    }

    [Fact]
    public void A_caret_on_a_line_the_diff_leaves_out_maps_to_nothing()
    {
        var proposal = Propose(new FakeEditorView(Text));

        Assert.Null(proposal.ToDiff(TextSelection.Caret(10)));
    }

    [Theory]
    // A kept line maps column for column.
    [InlineData(54, 4)]
    // An added line that replaces a removed one maps to the removed line.
    [InlineData(51, 2)]
    // A header maps to where its hunk starts.
    [InlineData(30, 0)]
    public void A_diff_position_maps_to_the_document(int diffPosition, int documentPosition)
    {
        var proposal = Propose(new FakeEditorView(Text));

        Assert.Equal(TextSelection.Caret(documentPosition), proposal.ToDocument(TextSelection.Caret(diffPosition)));
    }

    [Fact]
    public void Positions_follow_lines_added_above_the_change()
    {
        var editor = new FakeEditorView(Text);
        var proposal = Propose(editor);

        editor.Replace(new TextRange(0, 0), "new\n");

        Assert.Equal(TextSelection.Caret(6), proposal.ToDocument(TextSelection.Caret(proposal.Diff.IndexOf("-b", StringComparison.Ordinal) + 1)));
    }
}
