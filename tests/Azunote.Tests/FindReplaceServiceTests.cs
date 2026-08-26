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
    public void Count_and_replace_all_use_non_overlapping_case_insensitive_matches()
    {
        Assert.Equal(2, FindReplaceService.Count("Foo foo", "foo"));
        Assert.Equal("bar bar", FindReplaceService.ReplaceAll("Foo foo", "foo", "bar"));
    }

    [Fact]
    public void Empty_query_is_a_no_op()
    {
        Assert.Null(FindReplaceService.FindNext("text", "", 0));
        Assert.Equal(0, FindReplaceService.Count("text", ""));
        Assert.Equal("text", FindReplaceService.ReplaceAll("text", "", "replacement"));
    }
}
