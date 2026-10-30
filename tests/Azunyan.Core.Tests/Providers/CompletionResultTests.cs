using Azunyan.Core;
using Xunit;

namespace Azunyan.Core.Tests;

public sealed class CompletionResultTests
{
    [Fact]
    public void A_completion_word_matches_from_a_word_start_skipping_characters()
    {
        Assert.NotNull(FuzzyMatch.MatchCompletion("wl", "WriteLine"));
        Assert.NotNull(FuzzyMatch.MatchCompletion("line", "WriteLine"));
        Assert.NotNull(FuzzyMatch.MatchCompletion("WRL", "WriteLine"));
        Assert.Null(FuzzyMatch.MatchCompletion("rite", "Write"));
        Assert.Null(FuzzyMatch.MatchCompletion("lw", "WriteLine"));
        Assert.Equal(0, FuzzyMatch.MatchCompletion(string.Empty, "Write"));
    }

    [Fact]
    public void Narrowing_keeps_the_matching_items_best_first_then_in_their_order()
    {
        var result = new CompletionResult(
            TextRange.FromBounds(8, 8),
            [
                new CompletionItem("BackgroundColor"),
                new CompletionItem("Write"),
                new CompletionItem("WriteLine"),
                new CompletionItem("html", filterText: "\"html"),
                new CompletionItem("WindowLeft"),
            ]);

        var narrowed = result.Narrow(TextRange.FromBounds(8, 10), "wl");

        Assert.Equal(TextRange.FromBounds(8, 10), narrowed.ReplacementRange);
        Assert.Equal(["WriteLine", "WindowLeft"], narrowed.Items.Select(item => item.Label));
        Assert.Equal(
            ["Write", "WriteLine", "WindowLeft"],
            result.Narrow(TextRange.FromBounds(8, 9), "w").Items.Select(item => item.Label));
        Assert.Equal(
            ["WindowLeft", "WriteLine"],
            result.Narrow(TextRange.FromBounds(8, 11), "wil").Items.Select(item => item.Label));
        Assert.Equal(["html"], result.Narrow(TextRange.FromBounds(8, 10), "ht").Items.Select(item => item.Label));
    }
}
