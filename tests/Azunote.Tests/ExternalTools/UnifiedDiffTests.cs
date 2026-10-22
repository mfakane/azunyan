using Azunote;
using Xunit;

namespace Azunote.Tests.ExternalTools;

public sealed class UnifiedDiffTests
{
    [Fact]
    public void Same_lines_make_no_diff_whatever_their_line_endings()
    {
        Assert.Equal(string.Empty, UnifiedDiff.Create("a\r\nb\r\n", "a\nb\n", "old", "new"));
    }

    [Fact]
    public void A_change_is_shown_with_three_lines_of_context()
    {
        var diff = UnifiedDiff.Create(
            "1\n2\n3\n4\n5\n6\n7\n8\n9\n",
            "1\n2\n3\n4\nfive\n6\n7\n8\n9\n",
            "a/x",
            "b/x");

        Assert.Equal(
            "--- a/x\n+++ b/x\n@@ -2,7 +2,7 @@\n 2\n 3\n 4\n-5\n+five\n 6\n 7\n 8\n",
            diff);
    }

    [Fact]
    public void Changes_far_apart_make_separate_hunks_and_near_ones_share_one()
    {
        var oldText = string.Join('\n', Enumerable.Range(1, 20)) + "\n";
        var newText = oldText.Replace("\n2\n", "\ntwo\n").Replace("\n18\n", "\neighteen\n");

        var diff = UnifiedDiff.Create(oldText, newText, "old", "new");

        Assert.Equal(["@@ -1,5 +1,5 @@", "@@ -15,6 +15,6 @@"], diff.Split('\n').Where(line => line.StartsWith("@@")));
    }

    [Fact]
    public void Added_and_removed_lines_are_numbered_from_the_first_line_given()
    {
        var diff = UnifiedDiff.Create("a\nb\n", "a\nx\nb\nc\n", "old", "new", firstLine: 10);

        Assert.Equal("--- old\n+++ new\n@@ -10,2 +10,4 @@\n a\n+x\n b\n+c\n", diff);
    }

    [Fact]
    public void Applying_the_diff_to_the_old_text_gives_the_new_text()
    {
        var random = new Random(1234);
        for (var run = 0; run < 300; run++)
        {
            string Make() => string.Concat(Enumerable.Range(0, random.Next(0, 30))
                .Select(_ => "abcde"[random.Next(5)] + "\n"));
            var oldText = Make();
            var newText = Make();

            var diff = UnifiedDiff.Create(oldText, newText, "old", "new");

            Assert.Equal(newText, Apply(oldText, diff));
        }
    }

    // Applies hunks by their old-side line numbers, checking the kept and
    // removed lines against the old text.
    private static string Apply(string oldText, string diff)
    {
        var oldLines = oldText.Split('\n')[..^1];
        if (diff.Length == 0)
        {
            return oldText;
        }

        var result = new List<string>();
        var next = 0;
        var lines = diff.Split('\n')[2..^1];
        for (var index = 0; index < lines.Length; index++)
        {
            var header = lines[index].Split(' ')[1][1..].Split(',');
            var start = int.Parse(header[0]);
            var count = header.Length > 1 ? int.Parse(header[1]) : 1;
            var from = count == 0 ? start : start - 1;
            result.AddRange(oldLines[next..from]);
            next = from;
            while (index + 1 < lines.Length && !lines[index + 1].StartsWith("@@", StringComparison.Ordinal))
            {
                var line = lines[++index];
                if (line[0] == '+')
                {
                    result.Add(line[1..]);
                    continue;
                }

                Assert.Equal(oldLines[next], line[1..]);
                next++;
                if (line[0] == ' ')
                {
                    result.Add(line[1..]);
                }
            }
        }

        result.AddRange(oldLines[next..]);
        return string.Concat(result.Select(line => line + "\n"));
    }

    [Fact]
    public void Text_added_to_an_empty_one_starts_at_line_zero_on_the_empty_side()
    {
        Assert.Equal("--- old\n+++ new\n@@ -0,0 +1 @@\n+x\n", UnifiedDiff.Create(string.Empty, "x", "old", "new"));
    }
}
