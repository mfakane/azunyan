using Azunyan.Core;
using Xunit;

namespace Azunyan.Core.Tests;

public sealed class ProviderSchedulerTests
{
    [Fact]
    public void Provider_configuration_captures_one_consistent_set_of_references()
    {
        var first = new DelegateSyntaxProvider(_ =>
            Task.FromResult<IEnumerable<SyntaxSpan>>(Array.Empty<SyntaxSpan>()));
        var second = new DelegateSyntaxProvider(_ =>
            Task.FromResult<IEnumerable<SyntaxSpan>>(Array.Empty<SyntaxSpan>()));
        var providers = new EditorProviderSet { Syntax = first };

        var configuration = providers.CreateSnapshot();
        providers.Syntax = second;

        Assert.Same(first, configuration.Syntax);
        Assert.Same(second, providers.Syntax);
    }

    [Fact]
    public void Completion_items_are_not_mutable_through_the_result()
    {
        var result = new CompletionResult(
            TextRange.Empty(0),
            new[] { new CompletionItem("word") });

        var items = Assert.IsAssignableFrom<IList<CompletionItem>>(result.Items);

        Assert.Throws<NotSupportedException>(() => items.Add(new CompletionItem("other")));
    }

    [Fact]
    public async Task Document_viewport_and_position_requests_have_independent_lifetimes()
    {
        var documentStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDocument = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var providers = new EditorProviderSet
        {
            Syntax = new DelegateSyntaxProvider(async context =>
            {
                documentStarted.SetResult();
                await releaseDocument.Task;
                return new[] { new SyntaxSpan(TextRange.Empty(0), context.Snapshot.Text) };
            }),
            Inlay = new DelegateInlayProvider(context =>
            {
                Assert.Equal(new TextRange(0, 5), context.VisibleRange);
                return new[]
                {
                    new InlineAdornment(
                        "hint",
                        DocumentAnchor.Before(5),
                        "parameter",
                        new AdornmentContent(": int"))
                };
            }),
            Completion = new DelegateCompletionProvider(context =>
                new CompletionResult(
                    TextRange.Empty(context.Position),
                    new[] { new CompletionItem("word") }))
        };
        using var scheduler = new EditorProviderScheduler(providers);
        var snapshot = new TextSnapshot("hello");

        var documentTask = scheduler.RequestDocumentAsync(snapshot, TextSelection.Caret(0));
        await documentStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var viewportTask = scheduler.RequestViewportAsync(
            snapshot,
            new TextRange(0, 5),
            TextSelection.Caret(1));
        var positionTask = scheduler.RequestPositionAsync(snapshot, 1, TextSelection.Caret(1));

        var viewport = await viewportTask.WaitAsync(TimeSpan.FromSeconds(1));
        var position = await positionTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.NotNull(viewport);
        Assert.Single(viewport!.Inlays);
        Assert.NotNull(position);
        Assert.Equal("word", Assert.Single(position!.Completions!.Items).Label);
        Assert.False(documentTask.IsCompleted);

        releaseDocument.SetResult();
        var document = await documentTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.NotNull(document);
        Assert.Equal("hello", Assert.Single(document!.Syntax).Classification);
    }

    [Fact]
    public async Task A_slow_provider_cannot_publish_after_a_new_request_on_the_same_channel()
    {
        var oldStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOld = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var providers = new EditorProviderSet
        {
            Completion = new DelegateCompletionProvider(async context =>
            {
                if (context.Snapshot.Text == "old")
                {
                    oldStarted.SetResult();
                    await releaseOld.Task;
                }

                return new CompletionResult(
                    TextRange.Empty(context.Position),
                    new[] { new CompletionItem(context.Snapshot.Text) });
            })
        };
        using var scheduler = new EditorProviderScheduler(providers);

        var oldTask = scheduler.RequestPositionAsync(
            new TextSnapshot("old"),
            0,
            TextSelection.Caret(0));
        await oldStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var newTask = scheduler.RequestPositionAsync(
            new TextSnapshot("new"),
            0,
            TextSelection.Caret(0));
        var newResults = await newTask.WaitAsync(TimeSpan.FromSeconds(1));

        releaseOld.SetResult();
        var oldResults = await oldTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.NotNull(newResults);
        Assert.Equal("new", Assert.Single(newResults!.Completions!.Items).Label);
        Assert.Null(oldResults);
    }

    [Fact]
    public async Task Cancel_all_returns_null_even_when_a_provider_observes_cancellation_late()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var providers = new EditorProviderSet
        {
            Tooltip = new DelegateTooltipProvider(async (_, cancellationToken) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return null;
            })
        };
        using var scheduler = new EditorProviderScheduler(providers);

        var request = scheduler.RequestPositionAsync(
            new TextSnapshot("hello"),
            0,
            TextSelection.Caret(0));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        scheduler.CancelAll();

        Assert.Null(await request.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task A_provider_exception_is_isolated_from_other_results_in_the_same_channel()
    {
        var providers = new EditorProviderSet
        {
            Syntax = new ThrowingSyntaxProvider(),
            Decorations = new DelegateDecorationProvider(_ =>
                new[] { new TextDecoration(TextRange.Empty(0), "diagnostic") })
        };
        using var scheduler = new EditorProviderScheduler(providers);

        var results = await scheduler.RequestDocumentAsync(
            new TextSnapshot("hello"),
            TextSelection.Caret(0));

        Assert.NotNull(results);
        Assert.Empty(results!.Syntax);
        Assert.Equal("diagnostic", Assert.Single(results.Decorations).Kind);
    }

    [Fact]
    public async Task Position_request_can_skip_completion_provider()
    {
        var calls = 0;
        var providers = new EditorProviderSet
        {
            Completion = new DelegateCompletionProvider(_ =>
            {
                Interlocked.Increment(ref calls);
                return new CompletionResult(
                    TextRange.Empty(0),
                    new[] { new CompletionItem("word") });
            })
        };
        using var scheduler = new EditorProviderScheduler(providers);

        var results = await scheduler.RequestPositionAsync(
            new TextSnapshot("hello"),
            0,
            TextSelection.Caret(0),
            includeCompletion: false);

        Assert.NotNull(results);
        Assert.Null(results!.Completions);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Incremental_syntax_provider_receives_the_previous_snapshot_and_change()
    {
        var previous = new TextSnapshot("old");
        var current = new TextSnapshot("new");
        var providers = new EditorProviderSet
        {
            Syntax = new DelegateIncrementalSyntaxProvider((context, oldSnapshot, change) =>
            {
                Assert.Same(current, context.Snapshot);
                Assert.Same(previous, oldSnapshot);
                Assert.Equal(new TextRange(0, 3), change.OldRange);
                Assert.Equal("new", change.NewText);
                return Task.FromResult<IEnumerable<SyntaxSpan>>(
                    new[] { new SyntaxSpan(new TextRange(0, 3), "incremental") });
            })
        };
        using var scheduler = new EditorProviderScheduler(providers);

        var previousResult = await scheduler.RequestDocumentAsync(
            previous,
            TextSelection.Caret(3));

        var result = await scheduler.RequestDocumentAsync(
            current,
            TextSelection.Caret(3),
            previousResults: previousResult,
            change: new TextChange(new TextRange(0, 3), "old", "new"));

        Assert.NotNull(result);
        Assert.Equal("incremental", Assert.Single(result!.Syntax).Classification);
    }

    private sealed class DelegateSyntaxProvider : ISyntaxProvider
    {
        private readonly Func<EditorProviderContext, Task<IEnumerable<SyntaxSpan>>> _handler;

        public DelegateSyntaxProvider(Func<EditorProviderContext, Task<IEnumerable<SyntaxSpan>>> handler) =>
            _handler = handler;

        public async ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
            EditorProviderContext context,
            CancellationToken cancellationToken = default) =>
            (await _handler(context)).ToArray();
    }

    private sealed class DelegateIncrementalSyntaxProvider : IIncrementalSyntaxProvider
    {
        private readonly Func<
            EditorProviderContext,
            TextSnapshot,
            TextChange,
            Task<IEnumerable<SyntaxSpan>>> _handler;

        public DelegateIncrementalSyntaxProvider(
            Func<EditorProviderContext, TextSnapshot, TextChange, Task<IEnumerable<SyntaxSpan>>> handler) =>
            _handler = handler;

        public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
            EditorProviderContext context,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<SyntaxAnalysis> GetSyntaxAnalysisAsync(
            EditorProviderContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(SyntaxAnalysis.Empty);

        public async ValueTask<SyntaxAnalysis> GetSyntaxAsync(
            EditorProviderContext context,
            TextSnapshot previousSnapshot,
            TextChange change,
            SyntaxAnalysis previousAnalysis,
            CancellationToken cancellationToken = default) =>
            new((await _handler(context, previousSnapshot, change)).ToArray());
    }

    private sealed class DelegateInlayProvider : IInlayProvider
    {
        private readonly Func<EditorProviderContext, IEnumerable<InlineAdornment>> _handler;

        public DelegateInlayProvider(Func<EditorProviderContext, IEnumerable<InlineAdornment>> handler) =>
            _handler = handler;

        public ValueTask<IReadOnlyList<InlineAdornment>> GetInlaysAsync(
            EditorProviderContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<InlineAdornment>>(_handler(context).ToArray());
    }

    private sealed class DelegateDecorationProvider : IDecorationProvider
    {
        private readonly Func<EditorProviderContext, IEnumerable<TextDecoration>> _handler;

        public DelegateDecorationProvider(Func<EditorProviderContext, IEnumerable<TextDecoration>> handler) =>
            _handler = handler;

        public ValueTask<IReadOnlyList<TextDecoration>> GetDecorationsAsync(
            EditorProviderContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<TextDecoration>>(_handler(context).ToArray());
    }

    private sealed class ThrowingSyntaxProvider : ISyntaxProvider
    {
        public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
            EditorProviderContext context,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("test provider failure");
    }

    private sealed class DelegateCompletionProvider : ICompletionProvider
    {
        private readonly Func<EditorProviderContext, Task<CompletionResult?>> _handler;

        public DelegateCompletionProvider(Func<EditorProviderContext, CompletionResult?> handler) =>
            _handler = context => Task.FromResult(handler(context));

        public DelegateCompletionProvider(Func<EditorProviderContext, Task<CompletionResult?>> handler) =>
            _handler = handler;

        public ValueTask<CompletionResult?> GetCompletionsAsync(
            EditorProviderContext context,
            CancellationToken cancellationToken = default) =>
            new(_handler(context));
    }

    private sealed class DelegateTooltipProvider : ITooltipProvider
    {
        private readonly Func<EditorProviderContext, CancellationToken, Task<TooltipData?>> _handler;

        public DelegateTooltipProvider(
            Func<EditorProviderContext, CancellationToken, Task<TooltipData?>> handler) =>
            _handler = handler;

        public ValueTask<TooltipData?> GetTooltipAsync(
            EditorProviderContext context,
            CancellationToken cancellationToken = default) =>
            new(_handler(context, cancellationToken));
    }
}
