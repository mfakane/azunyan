using Azunyan.Core;
using Xunit;

namespace Azunyan.Core.Tests;

public sealed class FoldingAnalysisSchedulerTests
{
    [Fact]
    public async Task Ordinary_folding_provider_results_are_complete()
    {
        var providers = new EditorProviderSet
        {
            Folding = new DelegateFoldingProvider(_ => new[] { new FoldRange("fold", new TextRange(0, 2)) })
        };
        using var scheduler = new EditorProviderScheduler(providers);

        var result = await scheduler.RequestDocumentAsync(new TextSnapshot("ab"), TextSelection.Caret(0));

        Assert.NotNull(result);
        Assert.True(result!.FoldsAreComplete);
        Assert.Single(result.Folds);
    }

    [Fact]
    public async Task Analysis_provider_can_publish_incomplete_results_and_exceptions_are_incomplete()
    {
        var incomplete = new EditorProviderSet
        {
            Folding = new DelegateFoldingAnalysisProvider(_ => new FoldingAnalysis(Array.Empty<FoldRange>(), false))
        };
        using var firstScheduler = new EditorProviderScheduler(incomplete);
        var first = await firstScheduler.RequestDocumentAsync(new TextSnapshot("ab"), TextSelection.Caret(0));

        var failing = new EditorProviderSet { Folding = new ThrowingFoldingProvider() };
        using var secondScheduler = new EditorProviderScheduler(failing);
        var second = await secondScheduler.RequestDocumentAsync(new TextSnapshot("ab"), TextSelection.Caret(0));

        Assert.False(first!.FoldsAreComplete);
        Assert.False(second!.FoldsAreComplete);
        Assert.Empty(second.Folds);
    }

    [Fact]
    public async Task Folding_analysis_cancellation_returns_no_result()
    {
        var providers = new EditorProviderSet
        {
            Folding = new DelegateFoldingAnalysisProvider(async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new FoldingAnalysis();
            })
        };
        using var scheduler = new EditorProviderScheduler(providers);
        using var cancellation = new CancellationTokenSource();
        var request = scheduler.RequestDocumentAsync(
            new TextSnapshot("ab"), TextSelection.Caret(0), cancellationToken: cancellation.Token);
        cancellation.Cancel();

        Assert.Null(await request);
    }

    private sealed class DelegateFoldingProvider : IFoldingProvider
    {
        private readonly Func<EditorProviderContext, IReadOnlyList<FoldRange>> _handler;

        public DelegateFoldingProvider(Func<EditorProviderContext, IReadOnlyList<FoldRange>> handler) => _handler = handler;

        public ValueTask<IReadOnlyList<FoldRange>> GetFoldsAsync(EditorProviderContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_handler(context));
    }

    private sealed class DelegateFoldingAnalysisProvider : IFoldingAnalysisProvider
    {
        private readonly Func<EditorProviderContext, CancellationToken, ValueTask<FoldingAnalysis>> _handler;

        public DelegateFoldingAnalysisProvider(Func<EditorProviderContext, FoldingAnalysis> handler) =>
            _handler = (context, _) => ValueTask.FromResult(handler(context));

        public DelegateFoldingAnalysisProvider(Func<EditorProviderContext, CancellationToken, ValueTask<FoldingAnalysis>> handler) => _handler = handler;

        public ValueTask<IReadOnlyList<FoldRange>> GetFoldsAsync(EditorProviderContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<FoldRange>>(Array.Empty<FoldRange>());

        public ValueTask<FoldingAnalysis> GetFoldingAnalysisAsync(EditorProviderContext context, CancellationToken cancellationToken = default) =>
            _handler(context, cancellationToken);
    }

    private sealed class ThrowingFoldingProvider : IFoldingProvider
    {
        public ValueTask<IReadOnlyList<FoldRange>> GetFoldsAsync(EditorProviderContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();
    }
}
