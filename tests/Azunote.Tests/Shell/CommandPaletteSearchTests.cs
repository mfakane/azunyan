using Azunote;
using Xunit;

namespace Azunote.Tests.Shell;

public sealed class CommandPaletteSearchTests
{
    [Fact]
    public void Match_prefers_word_starts()
    {
        var match = CommandPaletteSearch.Match("mlu", "Move Line Up");

        Assert.NotNull(match);
        Assert.Equal([0, 5, 10], match.Value.Positions);
    }

    [Fact]
    public void Match_ignores_case_and_rejects_characters_out_of_order()
    {
        Assert.NotNull(CommandPaletteSearch.Match("SAVE", "Save As..."));
        Assert.Null(CommandPaletteSearch.Match("ulm", "Move Line Up"));
    }

    [Fact]
    public void Match_treats_a_camel_case_boundary_as_a_word_start()
    {
        var match = CommandPaletteSearch.Match("dc", "DocumentController.cs");

        Assert.NotNull(match);
        Assert.Equal([0, 8], match.Value.Positions);
    }

    [Fact]
    public void Filter_ranks_word_start_and_consecutive_matches_first()
    {
        var items = new[] { Item("Delete Line"), Item("Move Line Down"), Item("Go to Line...") };

        var titles = CommandPaletteSearch.Filter("line", items)
            .Select(match => match.Item.Title)
            .ToArray();

        // Every title contains "Line" at a word start; the shortest wins ties.
        Assert.Equal(["Delete Line", "Go to Line...", "Move Line Down"], titles);
    }

    [Fact]
    public void Filter_matches_the_detail_after_titles_and_highlights_only_the_title()
    {
        var lf = Item("LF", detail: "View › Line Ending");
        var lineFeed = Item("Line Feed Info");

        var matches = CommandPaletteSearch.Filter("line ending lf", [lf, lineFeed]);

        var match = Assert.Single(matches);
        Assert.Same(lf, match.Item);
        Assert.Equal([0, 1], match.TitlePositions);
    }

    [Fact]
    public void Filter_without_query_keeps_every_item_in_order()
    {
        var items = new[] { Item("b"), Item("a") };

        var matches = CommandPaletteSearch.Filter(string.Empty, items);

        Assert.Equal(items, matches.Select(match => match.Item));
    }

    private static CommandPaletteItem Item(string title, string detail = "") =>
        new(title, detail, null, true, false, () => { });
}
