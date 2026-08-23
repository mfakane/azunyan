using Azunyan.Core;
using Xunit;

namespace Azunyan.Core.Tests;

public sealed class ProviderFrameTests
{
    [Fact]
    public async Task A_frame_rejects_results_from_a_different_snapshot()
    {
        var snapshot = new TextSnapshot("current");
        var other = new TextSnapshot("other");
        using var scheduler = new EditorProviderScheduler(new EditorProviderSet());
        var result = (await scheduler.RequestDocumentAsync(other, TextSelection.Caret(0)))!;

        Assert.Throws<ArgumentException>(() =>
            new EditorProviderFrame(snapshot, TextSelection.Caret(0), result));
    }

    [Fact]
    public async Task Legacy_projection_keeps_partial_channels_snapshot_bound()
    {
        var snapshot = new TextSnapshot("hello");
        using var scheduler = new EditorProviderScheduler(new EditorProviderSet());
        var frame = new EditorProviderFrame(
            snapshot,
            TextSelection.Caret(2),
            document: await scheduler.RequestDocumentAsync(snapshot, TextSelection.Caret(2)));

        var legacy = frame.ToLegacyResults(42);

        Assert.Equal(42, legacy.RequestId);
        Assert.Same(snapshot, legacy.Context.Snapshot);
        Assert.Equal(2, legacy.Context.Position);
        Assert.Empty(legacy.Decorations);
        Assert.Null(legacy.Tooltip);
        Assert.Null(legacy.Completions);
        Assert.Empty(legacy.Gutter);
    }

}
