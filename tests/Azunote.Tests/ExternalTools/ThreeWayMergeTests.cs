using Azunote;
using Xunit;

namespace Azunote.Tests.ExternalTools;

public sealed class ThreeWayMergeTests
{
    private const string Base = "a\nb\nc\nd\ne\nf\n";

    [Fact]
    public void Without_edits_the_proposal_is_taken_in_the_line_endings_of_the_text()
    {
        var result = ThreeWayMerge.Merge(Base, Base, "a\r\nB\r\n");

        Assert.Equal("a\nB\n", result.Text);
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public void Changes_to_different_lines_both_stand()
    {
        var result = ThreeWayMerge.Merge(Base, "a\nb\nc\nd\nE\nf\n", "a\nB\nc\nd\ne\nf\n");

        Assert.Equal("a\nB\nc\nd\nE\nf\n", result.Text);
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public void Changes_on_neighbouring_lines_both_stand()
    {
        var result = ThreeWayMerge.Merge(Base, "a\nb\nC\nd\ne\nf\n", "a\nB\nc\nd\ne\nf\n");

        Assert.Equal("a\nB\nC\nd\ne\nf\n", result.Text);
    }

    [Fact]
    public void Lines_added_above_move_the_proposed_change_down()
    {
        var result = ThreeWayMerge.Merge(Base, "new\nnew\n" + Base, "a\nb\nc\nd\nE\nf\n");

        Assert.Equal("new\nnew\na\nb\nc\nd\nE\nf\n", result.Text);
    }

    [Fact]
    public void A_proposed_change_to_an_edited_line_is_left_out_and_reported()
    {
        var result = ThreeWayMerge.Merge(Base, "a\nb\nmine\nd\ne\nf\n", "a\nb\ntheirs\nd\ne\nF\n");

        Assert.Equal("a\nb\nmine\nd\ne\nF\n", result.Text);
        Assert.Equal([new ThreeWayMerge.Conflict(2, 3)], result.Conflicts);
    }

    [Fact]
    public void A_proposal_that_changes_nothing_leaves_the_edits_as_they_are()
    {
        var random = new Random(42);
        for (var run = 0; run < 200; run++)
        {
            string Make() => string.Concat(Enumerable.Range(0, random.Next(0, 20))
                .Select(_ => "abc"[random.Next(3)] + "\n"));
            var baseText = Make();
            var edited = Make();

            var result = ThreeWayMerge.Merge(baseText, edited, baseText);

            Assert.Equal(edited, result.Text);
            Assert.Empty(result.Conflicts);
        }
    }

    [Fact]
    public void Edits_keep_the_line_endings_of_the_edited_text()
    {
        var result = ThreeWayMerge.Merge("a\r\nb\r\nc\r\n", "a\r\nb\r\nc\r\nd\r\n", "A\nb\nc\n");

        Assert.Equal("A\r\nb\r\nc\r\nd\r\n", result.Text);
    }
}
