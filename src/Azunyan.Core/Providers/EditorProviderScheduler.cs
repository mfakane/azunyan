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
        Syntax = Array.AsReadOnly(syntax.ToArray());
        Decorations = Array.AsReadOnly(decorations.ToArray());
        Folds = Array.AsReadOnly(folds.ToArray());
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
        Gutter = Array.AsReadOnly(gutter.ToArray());
        Inlays = Array.AsReadOnly(inlays.ToArray());
        BlockAdornments = Array.AsReadOnly(blockAdornments.ToArray());
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
        CancellationToken cancellationToken = default) =>
        RequestDocumentAsync(
            snapshot,
            selection,
            previousSnapshot: null,
            change: null,
            cancellationToken: cancellationToken);

    public Task<DocumentProviderResults?> RequestDocumentAsync(
        TextSnapshot snapshot,
        TextSelection selection,
        TextSnapshot? previousSnapshot,
        TextChange? change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var context = new EditorProviderContext(snapshot, selection.CaretPosition, selection);
        var providers = _providers.CreateSnapshot();
        return RequestAsync(
            _document,
            context,
            (requestId, providerContext, token) =>
                CollectDocumentAsync(
                    providers,
                    previousSnapshot,
                    change,
                    requestId,
                    providerContext,
                    token),
            cancellationToken);
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
        var providers = _providers.CreateSnapshot();
        return RequestAsync(
            _viewport,
            context,
            (requestId, providerContext, token) =>
                CollectViewportAsync(providers, requestId, providerContext, token),
            cancellationToken);
    }

    public Task<PositionProviderResults?> RequestPositionAsync(
        TextSnapshot snapshot,
        int position,
        TextSelection selection,
        bool includeCompletion = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var context = new EditorProviderContext(snapshot, position, selection);
        var providers = _providers.CreateSnapshot();
        return RequestAsync(
            _position,
            context,
            (requestId, providerContext, token) =>
                CollectPositionAsync(
                    providers,
                    requestId,
                    providerContext,
                    includeCompletion,
                    token),
            cancellationToken);
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
        Func<long, EditorProviderContext, CancellationToken, Task<TResult>> collect,
        CancellationToken cancellationToken)
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

    private static async Task<DocumentProviderResults> CollectDocumentAsync(
        EditorProviderConfiguration providers,
        TextSnapshot? previousSnapshot,
        TextChange? change,
        long requestId,
        EditorProviderContext context,
        CancellationToken cancellationToken)
    {
        var syntaxTask = GetSyntaxAsync(
            providers.Syntax,
            previousSnapshot,
            change,
            context,
            cancellationToken);
        var decorationTask = providers.Decorations is null
            ? Task.FromResult<IReadOnlyList<TextDecoration>>(Array.Empty<TextDecoration>())
            : InvokeListAsync(
                () => providers.Decorations.GetDecorationsAsync(context, cancellationToken),
                cancellationToken);
        var foldingTask = providers.Folding is null
            ? Task.FromResult<IReadOnlyList<FoldRange>>(Array.Empty<FoldRange>())
            : InvokeListAsync(
                () => providers.Folding.GetFoldsAsync(context, cancellationToken),
                cancellationToken);

        await Task.WhenAll(syntaxTask, decorationTask, foldingTask).ConfigureAwait(false);
        return new DocumentProviderResults(
            context.Snapshot,
            syntaxTask.Result.Where(item => IsValidRange(item.Range, context.Snapshot)).ToArray(),
            decorationTask.Result.Where(item => IsValidRange(item.Range, context.Snapshot)).ToArray(),
            foldingTask.Result.Where(item => IsValidRange(item.Range, context.Snapshot)).ToArray());
    }

    private static Task<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        ISyntaxProvider? provider,
        TextSnapshot? previousSnapshot,
        TextChange? change,
        EditorProviderContext context,
        CancellationToken cancellationToken)
    {
        if (provider is null)
        {
            return Task.FromResult<IReadOnlyList<SyntaxSpan>>(Array.Empty<SyntaxSpan>());
        }

        if (provider is IIncrementalSyntaxProvider incremental
            && previousSnapshot is not null
            && change is { } documentChange
            && documentChange.OldRange.End <= previousSnapshot.Length
            && documentChange.NewRange.End <= context.Snapshot.Length)
        {
            return InvokeListAsync(
                () => incremental.GetSyntaxAsync(
                    context,
                    previousSnapshot,
                    documentChange,
                    cancellationToken),
                cancellationToken);
        }

        return InvokeListAsync(
            () => provider.GetSyntaxAsync(context, cancellationToken),
            cancellationToken);
    }

    private static async Task<ViewportProviderResults> CollectViewportAsync(
        EditorProviderConfiguration providers,
        long requestId,
        EditorProviderContext context,
        CancellationToken cancellationToken)
    {
        var gutterTask = providers.Gutter is null
            ? Task.FromResult<IReadOnlyList<GutterItem>>(Array.Empty<GutterItem>())
            : InvokeListAsync(
                () => providers.Gutter.GetGutterItemsAsync(context, cancellationToken),
                cancellationToken);
        var inlayTask = providers.Inlay is null
            ? Task.FromResult<IReadOnlyList<InlineAdornment>>(Array.Empty<InlineAdornment>())
            : InvokeListAsync(
                () => providers.Inlay.GetInlaysAsync(context, cancellationToken),
                cancellationToken);
        var blockTask = providers.BlockAdornment is null
            ? Task.FromResult<IReadOnlyList<BlockAdornment>>(Array.Empty<BlockAdornment>())
            : InvokeListAsync(
                () => providers.BlockAdornment.GetBlockAdornmentsAsync(context, cancellationToken),
                cancellationToken);

        await Task.WhenAll(gutterTask, inlayTask, blockTask).ConfigureAwait(false);
        var visibleRange = context.VisibleRange!.Value;
        return new ViewportProviderResults(
            context,
            gutterTask.Result.Where(item => item.Line < context.Snapshot.Lines.LineCount).ToArray(),
            inlayTask.Result.Where(item => ContainsAnchor(visibleRange, item.Anchor)).ToArray(),
            blockTask.Result.Where(item => ContainsAnchor(visibleRange, item.Anchor)).ToArray());
    }

    private static async Task<PositionProviderResults> CollectPositionAsync(
        EditorProviderConfiguration providers,
        long requestId,
        EditorProviderContext context,
        bool includeCompletion,
        CancellationToken cancellationToken)
    {
        var tooltipTask = providers.Tooltip is null
            ? Task.FromResult<TooltipData?>(null)
            : InvokeOptionalAsync(
                () => providers.Tooltip.GetTooltipAsync(context, cancellationToken),
                cancellationToken);
        var completionTask = !includeCompletion || providers.Completion is null
            ? Task.FromResult<CompletionResult?>(null)
            : InvokeOptionalAsync(
                () => providers.Completion.GetCompletionsAsync(context, cancellationToken),
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

    private static async Task CompleteWhenFinishedAsync<TResult>(
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
