namespace Azunyan.Core;

public sealed class DocumentProviderResults
{
    internal DocumentProviderResults(
        TextSnapshot snapshot,
        IReadOnlyList<SyntaxSpan> syntax,
        IReadOnlyList<TextDecoration> decorations,
        IReadOnlyList<FoldRange> folds)
    {
        Snapshot = snapshot;
        Syntax = syntax;
        Decorations = decorations;
        Folds = folds;
    }

    public TextSnapshot Snapshot { get; }

    public IReadOnlyList<SyntaxSpan> Syntax { get; }

    public IReadOnlyList<TextDecoration> Decorations { get; }

    public IReadOnlyList<FoldRange> Folds { get; }
}

public sealed class ViewportProviderResults
{
    internal ViewportProviderResults(
        EditorProviderContext context,
        IReadOnlyList<GutterItem> gutter,
        IReadOnlyList<InlineAdornment> inlays,
        IReadOnlyList<BlockAdornment> blockAdornments)
    {
        Context = context;
        Gutter = gutter;
        Inlays = inlays;
        BlockAdornments = blockAdornments;
    }

    public EditorProviderContext Context { get; }

    public IReadOnlyList<GutterItem> Gutter { get; }

    public IReadOnlyList<InlineAdornment> Inlays { get; }

    public IReadOnlyList<BlockAdornment> BlockAdornments { get; }
}

public sealed class PositionProviderResults
{
    internal PositionProviderResults(
        EditorProviderContext context,
        TooltipData? tooltip,
        CompletionResult? completions)
    {
        Context = context;
        Tooltip = tooltip;
        Completions = completions;
    }

    public EditorProviderContext Context { get; }

    public TooltipData? Tooltip { get; }

    public CompletionResult? Completions { get; }
}

/// <summary>
/// Runs provider groups with independent generations. A caret move only
/// invalidates tooltip/completion; syntax and folding remain snapshot-scoped.
/// </summary>
public sealed class EditorProviderScheduler : IDisposable
{
    private readonly EditorProviderSet _providers;
    private readonly object _gate = new();
    private readonly ChannelState _document = new();
    private readonly ChannelState _viewport = new();
    private readonly ChannelState _position = new();
    private bool _disposed;

    public EditorProviderScheduler(EditorProviderSet providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers;
    }

    public Task<DocumentProviderResults?> RequestDocumentAsync(
        TextSnapshot snapshot,
        TextSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var context = new EditorProviderContext(snapshot, selection.CaretPosition, selection);
        return RequestAsync(
            _document,
            context,
            cancellationToken,
            CollectDocumentAsync);
    }

    public Task<ViewportProviderResults?> RequestViewportAsync(
        TextSnapshot snapshot,
        TextRange visibleRange,
        TextSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var context = new EditorProviderContext(
            snapshot,
            selection.CaretPosition,
            selection,
            visibleRange);
        return RequestAsync(
            _viewport,
            context,
            cancellationToken,
            CollectViewportAsync);
    }

    public Task<PositionProviderResults?> RequestPositionAsync(
        TextSnapshot snapshot,
        int position,
        TextSelection selection,
        CancellationToken cancellationToken = default,
        bool includeCompletion = true)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var context = new EditorProviderContext(snapshot, position, selection);
        return RequestAsync(
            _position,
            context,
            cancellationToken,
            (requestId, providerContext, token) =>
                CollectPositionAsync(requestId, providerContext, includeCompletion, token));
    }

    public void CancelAll()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _document.Cancel();
            _viewport.Cancel();
            _position.Cancel();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _document.Cancel();
            _viewport.Cancel();
            _position.Cancel();
        }
    }

    private async Task<TResult?> RequestAsync<TResult>(
        ChannelState channel,
        EditorProviderContext context,
        CancellationToken cancellationToken,
        Func<long, EditorProviderContext, CancellationToken, Task<TResult>> collect)
        where TResult : class
    {
        RequestState request;
        lock (_gate)
        {
            ThrowIfDisposed();
            request = channel.Begin(cancellationToken);
        }

        var work = Task.Run(
            () => collect(request.Id, context, request.Token),
            request.Token);

        try
        {
            var canceled = Task.Delay(Timeout.InfiniteTimeSpan, request.Token);
            if (await Task.WhenAny(work, canceled).ConfigureAwait(false) != work)
            {
                return null;
            }

            var result = await work.ConfigureAwait(false);
            lock (_gate)
            {
                return !_disposed && channel.IsCurrent(request.Id) ? result : null;
            }
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            if (work.IsCompleted)
            {
                channel.Complete(request);
            }
            else
            {
                _ = CompleteWhenFinishedAsync(work, channel, request);
            }
        }
    }

    private async Task<DocumentProviderResults> CollectDocumentAsync(
        long requestId,
        EditorProviderContext context,
        CancellationToken cancellationToken)
    {
        var syntaxTask = _providers.Syntax is null
            ? Task.FromResult<IReadOnlyList<SyntaxSpan>>(Array.Empty<SyntaxSpan>())
            : InvokeListAsync(
                () => _providers.Syntax.GetSyntaxAsync(context, cancellationToken),
                cancellationToken);
        var decorationTask = _providers.Decorations is null
            ? Task.FromResult<IReadOnlyList<TextDecoration>>(Array.Empty<TextDecoration>())
            : InvokeListAsync(
                () => _providers.Decorations.GetDecorationsAsync(context, cancellationToken),
                cancellationToken);
        var foldingTask = _providers.Folding is null
            ? Task.FromResult<IReadOnlyList<FoldRange>>(Array.Empty<FoldRange>())
            : InvokeListAsync(
                () => _providers.Folding.GetFoldsAsync(context, cancellationToken),
                cancellationToken);

        await Task.WhenAll(syntaxTask, decorationTask, foldingTask).ConfigureAwait(false);
        return new DocumentProviderResults(
            context.Snapshot,
            syntaxTask.Result.Where(item => IsValidRange(item.Range, context.Snapshot)).ToArray(),
            decorationTask.Result.Where(item => IsValidRange(item.Range, context.Snapshot)).ToArray(),
            foldingTask.Result.Where(item => IsValidRange(item.Range, context.Snapshot)).ToArray());
    }

    private async Task<ViewportProviderResults> CollectViewportAsync(
        long requestId,
        EditorProviderContext context,
        CancellationToken cancellationToken)
    {
        var gutterTask = _providers.Gutter is null
            ? Task.FromResult<IReadOnlyList<GutterItem>>(Array.Empty<GutterItem>())
            : InvokeListAsync(
                () => _providers.Gutter.GetGutterItemsAsync(context, cancellationToken),
                cancellationToken);
        var inlayTask = _providers.Inlay is null
            ? Task.FromResult<IReadOnlyList<InlineAdornment>>(Array.Empty<InlineAdornment>())
            : InvokeListAsync(
                () => _providers.Inlay.GetInlaysAsync(context, cancellationToken),
                cancellationToken);
        var blockTask = _providers.BlockAdornment is null
            ? Task.FromResult<IReadOnlyList<BlockAdornment>>(Array.Empty<BlockAdornment>())
            : InvokeListAsync(
                () => _providers.BlockAdornment.GetBlockAdornmentsAsync(context, cancellationToken),
                cancellationToken);

        await Task.WhenAll(gutterTask, inlayTask, blockTask).ConfigureAwait(false);
        var visibleRange = context.VisibleRange!.Value;
        return new ViewportProviderResults(
            context,
            gutterTask.Result.Where(item => item.Line < context.Snapshot.Lines.LineCount).ToArray(),
            inlayTask.Result.Where(item => ContainsAnchor(visibleRange, item.Anchor)).ToArray(),
            blockTask.Result.Where(item => ContainsAnchor(visibleRange, item.Anchor)).ToArray());
    }

    private async Task<PositionProviderResults> CollectPositionAsync(
        long requestId,
        EditorProviderContext context,
        bool includeCompletion,
        CancellationToken cancellationToken)
    {
        var tooltipTask = _providers.Tooltip is null
            ? Task.FromResult<TooltipData?>(null)
            : InvokeOptionalAsync(
                () => _providers.Tooltip.GetTooltipAsync(context, cancellationToken),
                cancellationToken);
        var completionTask = !includeCompletion || _providers.Completion is null
            ? Task.FromResult<CompletionResult?>(null)
            : InvokeOptionalAsync(
                () => _providers.Completion.GetCompletionsAsync(context, cancellationToken),
                cancellationToken);

        await Task.WhenAll(tooltipTask, completionTask).ConfigureAwait(false);
        return new PositionProviderResults(
            context,
            tooltipTask.Result is { } tooltip && IsValidRange(tooltip.Range, context.Snapshot)
                ? tooltip
                : null,
            completionTask.Result is { } completions && IsValidRange(completions.ReplacementRange, context.Snapshot)
                ? completions
                : null);
    }

    private async Task CompleteWhenFinishedAsync<TResult>(
        Task<TResult> work,
        ChannelState channel,
        RequestState request)
    {
        try
        {
            await work.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The request was already canceled or superseded.
        }

        channel.Complete(request);
    }

    private static async Task<IReadOnlyList<T>> InvokeListAsync<T>(
        Func<ValueTask<IReadOnlyList<T>>> invoke,
        CancellationToken cancellationToken)
    {
        try
        {
            return await invoke().ConfigureAwait(false) ?? Array.Empty<T>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Array.Empty<T>();
        }
    }

    private static async Task<T?> InvokeOptionalAsync<T>(
        Func<ValueTask<T?>> invoke,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await invoke().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static bool IsValidRange(TextRange range, TextSnapshot snapshot) =>
        range.Start <= snapshot.Length && range.End <= snapshot.Length;

    private static bool ContainsAnchor(TextRange range, DocumentAnchor anchor) =>
        anchor.Position.Offset >= range.Start && anchor.Position.Offset <= range.End;

    private sealed class ChannelState
    {
        private long _nextId;
        private CancellationTokenSource? _active;

        public RequestState Begin(CancellationToken cancellationToken)
        {
            Cancel();
            var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var id = checked(_nextId + 1);
            _nextId = id;
            _active = source;
            return new RequestState(id, source);
        }

        public bool IsCurrent(long id) => id == _nextId;

        public void Cancel()
        {
            if (_active is not null)
            {
                _ = ObserveCancellationAsync(_active.CancelAsync());
                _active = null;
            }

            _nextId = checked(_nextId + 1);
        }

        public void Complete(RequestState request)
        {
            if (ReferenceEquals(_active, request.Source))
            {
                _active = null;
            }

            request.Source.Dispose();
        }

        private static async Task ObserveCancellationAsync(Task cancellation)
        {
            try
            {
                await cancellation.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Provider cancellation callbacks are isolated.
            }
        }
    }

    private sealed class RequestState
    {
        public RequestState(long id, CancellationTokenSource source)
        {
            Id = id;
            Source = source;
        }

        public long Id { get; }

        public CancellationTokenSource Source { get; }

        public CancellationToken Token => Source.Token;
    }
}
