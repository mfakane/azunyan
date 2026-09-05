using Xunit;

namespace Azunote.Tests;

public sealed class FindReplaceServiceTests
{
    [Fact]
    public void Find_next_wraps_after_the_end()
    {
        var match = FindReplaceService.FindNext("one two one", "one", 11);

        Assert.Equal(new SearchMatch(0, 3), match);
    }

    [Fact]
    public void Find_previous_wraps_before_the_start()
    {
        var match = FindReplaceService.FindPrevious("one two one", "one", 0);

        Assert.Equal(new SearchMatch(8, 3), match);
    }

    [Fact]
    public void Find_previous_starts_before_the_exclusive_boundary()
    {
        var match = FindReplaceService.FindPrevious("one two one", "one", 8);

        Assert.Equal(new SearchMatch(0, 3), match);
    }

    [Fact]
    public void Count_and_replace_all_use_non_overlapping_case_insensitive_matches()
    {
        Assert.Equal(2, FindReplaceService.Count("Foo foo", "foo"));
        Assert.Equal("bar bar", FindReplaceService.ReplaceAll("Foo foo", "foo", "bar"));
    }

    [Fact]
    public void Match_case_limits_literal_search_and_replace()
    {
        var options = FindReplaceOptions.MatchCase;

        Assert.Equal(
            new SearchMatch(4, 3),
            FindReplaceService.FindNext("Foo foo", "foo", 0, options));
        Assert.Equal(1, FindReplaceService.Count("Foo foo", "foo", options));
        Assert.Equal("Foo bar", FindReplaceService.ReplaceAll("Foo foo", "foo", "bar", options));
    }

    [Fact]
    public void Match_whole_word_ignores_matches_inside_words()
    {
        var options = FindReplaceOptions.MatchWholeWord;

        Assert.Equal(
            new SearchMatch(7, 3),
            FindReplaceService.FindNext("foobar foo", "foo", 0, options));
        Assert.Equal(1, FindReplaceService.Count("foobar foo", "foo", options));
        Assert.Equal(
            "foobar bar",
            FindReplaceService.ReplaceAll("foobar foo", "foo", "bar", options));
    }

    [Fact]
    public void Regular_expression_search_and_replace_support_capture_groups()
    {
        var options = FindReplaceOptions.RegularExpression;

        Assert.Equal(
            new SearchMatch(4, 2),
            FindReplaceService.FindNext("one 22 three", @"\d+", 0, options));
        Assert.Equal(1, FindReplaceService.Count("one 22 three", @"\d+", options));
        Assert.Equal(
            "one [22] three",
            FindReplaceService.ReplaceAll("one 22 three", @"(\d+)", "[$1]", options));
    }

    [Fact]
    public void Invalid_regular_expression_is_reported_as_invalid()
    {
        var options = FindReplaceOptions.RegularExpression;

        Assert.False(FindReplaceService.IsValidQuery("(", options));
        Assert.Null(FindReplaceService.FindNext("text", "(", 0, options));
    }

    [Fact]
    public void Empty_query_is_a_no_op()
    {
        Assert.Null(FindReplaceService.FindNext("text", "", 0));
        Assert.Equal(0, FindReplaceService.Count("text", ""));
        Assert.Equal("text", FindReplaceService.ReplaceAll("text", "", "replacement"));
    }
}
