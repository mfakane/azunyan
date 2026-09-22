using Azunyan.Core;
using Xunit;

namespace Azunyan.Core.Tests;

public sealed class FoldStateTrackerTests
{
    [Fact]
    public void Unaffected_folds_shift_and_touched_or_header_folds_are_invalidated()
    {
        var oldSnapshot = new TextSnapshot("head\nbody\nother\n");
        var newSnapshot = new TextSnapshot("prefix\nhead\nbody changed\nother\n");
        var change = new TextChange(new TextRange(0, 0), string.Empty, "prefix\n");
        var tracker = CreateTracker(oldSnapshot,
            new FoldRange("head", new TextRange(0, 9)),
            new FoldRange("other", new TextRange(10, 6)));

        tracker.ApplyTextChange(oldSnapshot, newSnapshot, change);

        Assert.Null(tracker.FindFold("head"));
        Assert.Equal(new TextRange(17, 6), Assert.Single(tracker.Folds).Range);
    }

    [Fact]
    public void Incomplete_empty_result_preserves_collapsed_but_complete_prunes_it()
    {
        var snapshot = new TextSnapshot("a\nb\n");
        var fold = new FoldRange("a", new TextRange(0, 3));
        var tracker = CreateTracker(snapshot, fold);
        tracker.SetCollapsed("a", true);

        tracker.ApplyProviderFolds(snapshot, Array.Empty<FoldRange>(), isComplete: false);
        Assert.Equal(new[] { "a" }, tracker.CollapsedIds);
        Assert.Same(fold, tracker.FindFold("a"));

        tracker.ApplyProviderFolds(snapshot, Array.Empty<FoldRange>(), isComplete: true);
        Assert.Empty(tracker.CollapsedIds);
        Assert.Empty(tracker.Folds);
    }

    [Fact]
    public void Exact_range_migrates_id_but_same_id_unrelated_range_does_not_move_collapse()
    {
        var snapshot = new TextSnapshot("one\ntwo\nthree\n");
        var original = new FoldRange("old", new TextRange(0, 7));
        var tracker = CreateTracker(snapshot, original);
        tracker.SetCollapsed("old", true);

        tracker.ApplyProviderFolds(snapshot,
            new[] { new FoldRange("new", original.Range) }, isComplete: true);
        Assert.Equal(new[] { "new" }, tracker.CollapsedIds);

        tracker.ApplyProviderFolds(snapshot,
            new[] { new FoldRange("new", new TextRange(8, 5)) }, isComplete: true);
        Assert.Empty(tracker.CollapsedIds);
    }

    [Fact]
    public void Incomplete_same_id_prefers_collapsed_semantic_fold_over_wrong_provider_range()
    {
        var snapshot = new TextSnapshot("one\ntwo\nthree\n");
        var original = new FoldRange("same", new TextRange(0, 7));
        var tracker = CreateTracker(snapshot, original);
        tracker.SetCollapsed("same", true);

        tracker.ApplyProviderFolds(snapshot,
            new[] { new FoldRange("same", new TextRange(8, 5)) }, isComplete: false);

        Assert.Equal(original.Range, Assert.Single(tracker.Folds).Range);
        Assert.Equal(new[] { "same" }, tracker.CollapsedIds);
    }

    [Fact]
    public void Incomplete_result_migrates_to_range_match_instead_of_reused_ordinal_id()
    {
        var snapshot = new TextSnapshot("one\ntwo\nthree\n");
        var original = new FoldRange("item:1", new TextRange(8, 5));
        var tracker = CreateTracker(snapshot, original);
        tracker.SetCollapsed(original.Id, true);

        tracker.ApplyProviderFolds(
            snapshot,
            [
                new FoldRange("item:1", new TextRange(0, 3)),
                new FoldRange("item:2", original.Range)
            ],
            isComplete: false);

        Assert.Equal(new[] { "item:2" }, tracker.CollapsedIds);
        Assert.Equal(original.Range, tracker.FindFold("item:2")?.Range);
        Assert.Null(tracker.FindFold("item:1"));
    }

    [Fact]
    public void Chained_edits_and_snapshot_mismatch_are_handled()
    {
        var first = new TextSnapshot("a\nb\nc\n");
        var second = new TextSnapshot("x\na\nb\nc\n");
        var third = new TextSnapshot("x\naa\nb\nc\n");
        var tracker = CreateTracker(first, new FoldRange("c", new TextRange(4, 2)));
        tracker.SetCollapsed("c", true);
        tracker.ApplyTextChange(first, second, new TextChange(TextRange.Empty(0), string.Empty, "x\n"));
        tracker.ApplyTextChange(second, third, new TextChange(new TextRange(3, 0), string.Empty, "a"));

        Assert.Equal(new TextRange(7, 2), Assert.Single(tracker.Folds).Range);
        Assert.Throws<InvalidOperationException>(() => tracker.ApplyProviderFolds(first, Array.Empty<FoldRange>(), true));
    }

    [Fact]
    public void Empty_tracker_still_rejects_a_change_that_does_not_match_the_snapshots()
    {
        var oldSnapshot = new TextSnapshot("old");
        var newSnapshot = new TextSnapshot("new");
        var tracker = new FoldStateTracker();
        tracker.Reset(oldSnapshot);

        Assert.Throws<ArgumentException>(() => tracker.ApplyTextChange(
            oldSnapshot,
            newSnapshot,
            new TextChange(new TextRange(0, 3), "bad", "new")));
    }

    private static FoldStateTracker CreateTracker(TextSnapshot snapshot, params FoldRange[] folds)
    {
        var tracker = new FoldStateTracker();
        tracker.Reset(snapshot);
        tracker.ApplyProviderFolds(snapshot, folds, isComplete: true);
        return tracker;
    }
}
