namespace Azunyan.Core;

/// <summary>
/// The immutable input supplied to every editor provider. Positions are UTF-16
/// offsets, matching <see cref="TextSnapshot"/> and the WinUI text controls.
/// </summary>
public sealed class EditorProviderContext
{
    public EditorProviderContext(
        TextSnapshot snapshot,
        int position,
        TextSelection selection,
        TextRange? visibleRange = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (position < 0 || position > snapshot.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        if (selection.Start > snapshot.Length || selection.End > snapshot.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(selection));
        }

        if (visibleRange is { } range
            && (range.Start > snapshot.Length || range.End > snapshot.Length))
        {
            throw new ArgumentOutOfRangeException(nameof(visibleRange));
        }

        Snapshot = snapshot;
        Position = position;
        Selection = selection;
        VisibleRange = visibleRange;
    }

    public TextSnapshot Snapshot { get; }

    public int Position { get; }

    public TextSelection Selection { get; }

    /// <summary>
    /// The requested document range for viewport-scoped providers. It is null
    /// for document- and position-scoped requests.
    /// </summary>
    public TextRange? VisibleRange { get; }

    public LineColumn Location => Snapshot.Lines.GetLineColumn(Position);
}

/// <summary>
/// A classified range returned by a syntax provider. Classification names are
/// intentionally strings so a consumer can define its own language taxonomy.
/// </summary>
public readonly record struct SyntaxSpan
{
    public SyntaxSpan(TextRange range, string classification)
    {
        ArgumentNullException.ThrowIfNull(classification);
        Range = range;
        Classification = classification;
    }

    public TextRange Range { get; }

    public string Classification { get; }
}

/// <summary>
/// A complete syntax result for one snapshot. <see cref="Spans"/> is the
/// result consumed by the renderer. <see cref="Candidates"/> retains the
/// source candidates needed by composite providers when they incrementally
/// recompute their result.
/// </summary>
public sealed class SyntaxAnalysis
{
    public SyntaxAnalysis(
        IReadOnlyList<SyntaxSpan>? spans = null,
        IReadOnlyList<SyntaxSpan>? candidates = null,
        SyntaxProviderState? state = null)
    {
        Spans = Array.AsReadOnly((spans ?? Array.Empty<SyntaxSpan>()).ToArray());
        Candidates = Array.AsReadOnly((candidates ?? Spans).ToArray());
        State = state;
    }

    public IReadOnlyList<SyntaxSpan> Spans { get; }

    public IReadOnlyList<SyntaxSpan> Candidates { get; }

    public SyntaxProviderState? State { get; }

    public static SyntaxAnalysis Empty { get; } = new();
}

/// <summary>
/// Provider-owned state carried from one complete syntax analysis to the next.
/// </summary>
public abstract record SyntaxProviderState;

/// <summary>
/// A range decoration returned by a decoration provider. Rendering remains a
/// consumer concern; <see cref="Kind"/> lets each consumer map a decoration
/// to its own brush, underline, diagnostic, or other visual treatment.
/// </summary>
public readonly record struct TextDecoration
{
    public TextDecoration(TextRange range, string kind, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(kind);
        Range = range;
        Kind = kind;
        Message = message;
    }

    public TextRange Range { get; }

    public string Kind { get; }

    public string? Message { get; }
}

/// <summary>
/// Content to show for a position in a snapshot. The range identifies the
/// symbol or text that caused the tooltip and is useful for anchoring it.
/// </summary>
public sealed record TooltipData
{
    public TooltipData(TextRange range, string content, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        Range = range;
        Content = content;
        Title = title;
    }

    public TextRange Range { get; }

    public string Content { get; }

    public string? Title { get; }
}

/// <summary>
/// One completion candidate. InsertText defaults to Label, which keeps simple
/// keyword and document-word providers small while allowing richer providers
/// to insert a different value.
/// </summary>
public sealed record CompletionItem
{
    public CompletionItem(
        string label,
        string? insertText = null,
        string? detail = null,
        string? documentation = null,
        int sortOrder = 0)
    {
        ArgumentNullException.ThrowIfNull(label);
        Label = label;
        InsertText = insertText ?? label;
        Detail = detail;
        Documentation = documentation;
        SortOrder = sortOrder;
    }

    public string Label { get; }

    public string InsertText { get; }

    public string? Detail { get; }

    public string? Documentation { get; }

    public int SortOrder { get; }
}

/// <summary>
/// Completion candidates and the range that should be replaced when one is
/// accepted.
/// </summary>
public sealed class CompletionResult
{
    public CompletionResult(TextRange replacementRange, IReadOnlyList<CompletionItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ReplacementRange = replacementRange;
        Items = Array.AsReadOnly(items.ToArray());
    }

    public TextRange ReplacementRange { get; }

    public IReadOnlyList<CompletionItem> Items { get; }

    public static CompletionResult Empty(TextRange replacementRange) =>
        new(replacementRange, Array.Empty<CompletionItem>());
}

/// <summary>
/// One item in the editor gutter. Line is zero based and refers to the
/// snapshot supplied in the provider request.
/// </summary>
public sealed record GutterItem
{
    public GutterItem(int line, string text, string? kind = null, string? toolTip = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(line);

        ArgumentNullException.ThrowIfNull(text);
        Line = line;
        Text = text;
        Kind = kind;
        ToolTip = toolTip;
    }

    public int Line { get; }

    public string Text { get; }

    public string? Kind { get; }

    public string? ToolTip { get; }
}

public interface ISyntaxProvider
{
    ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional incremental syntax contract. Implementations can use the previous
/// snapshot and the coarse document change to avoid rescanning unrelated text.
/// Providers that do not implement this interface continue to receive the
/// full snapshot through <see cref="ISyntaxProvider"/>.
/// </summary>
public interface IIncrementalSyntaxProvider : ISyntaxProvider
{
    ValueTask<SyntaxAnalysis> GetSyntaxAsync(
        EditorProviderContext context,
        TextSnapshot previousSnapshot,
        TextChange change,
        SyntaxAnalysis previousAnalysis,
        CancellationToken cancellationToken = default);
}

public interface IDecorationProvider
{
    ValueTask<IReadOnlyList<TextDecoration>> GetDecorationsAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default);
}

public interface ITooltipProvider
{
    ValueTask<TooltipData?> GetTooltipAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default);
}

public interface ICompletionProvider
{
    ValueTask<CompletionResult?> GetCompletionsAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default);
}

public interface IGutterProvider
{
    ValueTask<IReadOnlyList<GutterItem>> GetGutterItemsAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default);
}

public interface IFoldingProvider
{
    ValueTask<IReadOnlyList<FoldRange>> GetFoldsAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default);
}

public interface IInlayProvider
{
    ValueTask<IReadOnlyList<InlineAdornment>> GetInlaysAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default);
}

public interface IBlockAdornmentProvider
{
    ValueTask<IReadOnlyList<BlockAdornment>> GetBlockAdornmentsAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The provider set used by <see cref="EditorProviderCoordinator"/>. It is
/// deliberately a mutable container so an application can replace one
/// provider without rebuilding its editor host.
/// </summary>
public sealed class EditorProviderSet
{
    public ISyntaxProvider? Syntax { get; set; }

    public IDecorationProvider? Decorations { get; set; }

    public ITooltipProvider? Tooltip { get; set; }

    public ICompletionProvider? Completion { get; set; }

    public IGutterProvider? Gutter { get; set; }

    public IFoldingProvider? Folding { get; set; }

    public IInlayProvider? Inlay { get; set; }

    public IBlockAdornmentProvider? BlockAdornment { get; set; }

    /// <summary>
    /// Captures the current provider references for one request. The mutable
    /// set remains convenient for hosts, while a scheduler can use this
    /// snapshot to keep one request on one consistent configuration.
    /// </summary>
    public EditorProviderConfiguration CreateSnapshot() =>
        new(
            Syntax,
            Decorations,
            Tooltip,
            Completion,
            Gutter,
            Folding,
            Inlay,
            BlockAdornment);
}

/// <summary>
/// Immutable provider references used by one scheduler request.
/// </summary>
public sealed class EditorProviderConfiguration
{
    internal EditorProviderConfiguration(
        ISyntaxProvider? syntax,
        IDecorationProvider? decorations,
        ITooltipProvider? tooltip,
        ICompletionProvider? completion,
        IGutterProvider? gutter,
        IFoldingProvider? folding,
        IInlayProvider? inlay,
        IBlockAdornmentProvider? blockAdornment)
    {
        Syntax = syntax;
        Decorations = decorations;
        Tooltip = tooltip;
        Completion = completion;
        Gutter = gutter;
        Folding = folding;
        Inlay = inlay;
        BlockAdornment = blockAdornment;
    }

    public ISyntaxProvider? Syntax { get; }

    public IDecorationProvider? Decorations { get; }

    public ITooltipProvider? Tooltip { get; }

    public ICompletionProvider? Completion { get; }

    public IGutterProvider? Gutter { get; }

    public IFoldingProvider? Folding { get; }

    public IInlayProvider? Inlay { get; }

    public IBlockAdornmentProvider? BlockAdornment { get; }
}

/// <summary>
/// A consistent set of provider results for one snapshot and position.
/// </summary>
public sealed class EditorProviderResults
{
    internal EditorProviderResults(
        long requestId,
        EditorProviderContext context,
        IReadOnlyList<SyntaxSpan> syntax,
        IReadOnlyList<TextDecoration> decorations,
        TooltipData? tooltip,
        CompletionResult? completions,
        IReadOnlyList<GutterItem> gutter)
    {
        RequestId = requestId;
        Context = context;
        Syntax = Array.AsReadOnly(syntax.ToArray());
        Decorations = Array.AsReadOnly(decorations.ToArray());
        Tooltip = tooltip;
        Completions = completions;
        Gutter = Array.AsReadOnly(gutter.ToArray());
    }

    public long RequestId { get; }

    public EditorProviderContext Context { get; }

    public IReadOnlyList<SyntaxSpan> Syntax { get; }

    public IReadOnlyList<TextDecoration> Decorations { get; }

    public TooltipData? Tooltip { get; }

    public CompletionResult? Completions { get; }

    public IReadOnlyList<GutterItem> Gutter { get; }
}

public sealed class EditorProviderResultsEventArgs : EventArgs
{
    public EditorProviderResultsEventArgs(EditorProviderResults results)
    {
        ArgumentNullException.ThrowIfNull(results);
        Results = results;
    }

    public EditorProviderResults Results { get; }
}

/// <summary>
/// Runs providers away from the caller thread and keeps only the newest
/// request. Starting a new request cancels the previous one, and a provider
/// that ignores cancellation still cannot publish a stale result.
/// </summary>
public sealed class EditorProviderCoordinator : IDisposable
{
    private readonly EditorProviderSet _providers;
    private readonly object _gate = new();
    private CancellationTokenSource? _activeCancellation;
    private long _nextRequestId;
    private bool _disposed;

    public EditorProviderCoordinator(EditorProviderSet providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers;
    }

    /// <summary>
    /// Requests all configured providers. The method returns quickly without
    /// executing provider code on the caller thread. A superseded or canceled
    /// request returns <see langword="null"/>.
    /// </summary>
    public async Task<EditorProviderResults?> RequestAsync(
        TextSnapshot snapshot,
        int position,
        TextSelection? selection = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var requestSelection = selection ?? TextSelection.Caret(position);
        var context = new EditorProviderContext(snapshot, position, requestSelection);
        var providers = _providers.CreateSnapshot();
        var request = BeginRequest(cancellationToken);
        var work = Task.Run(
            () => CollectAsync(providers, request.Id, context, request.Token),
            request.Token);

        try
        {
            var canceled = Task.Delay(Timeout.InfiniteTimeSpan, request.Token);
            var completed = await Task.WhenAny(work, canceled).ConfigureAwait(false);
            if (completed != work)
            {
                return null;
            }

            var results = await work.ConfigureAwait(false);

            lock (_gate)
            {
                return !_disposed && request.Id == _nextRequestId
                    ? results
                    : null;
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
                CompleteRequest(request);
            }
            else
            {
                _ = CompleteWhenFinishedAsync(work, request);
            }
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _nextRequestId = checked(_nextRequestId + 1);
            CancelSource(_activeCancellation);
            _activeCancellation = null;
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
            _nextRequestId = checked(_nextRequestId + 1);
            CancelSource(_activeCancellation);
            _activeCancellation = null;
        }
    }

    private RequestState BeginRequest(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            CancelSource(_activeCancellation);

            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var id = checked(_nextRequestId + 1);
            _nextRequestId = id;
            _activeCancellation = linked;
            return new RequestState(id, linked);
        }
    }

    private static async Task<EditorProviderResults> CollectAsync(
        EditorProviderConfiguration providers,
        long requestId,
        EditorProviderContext context,
        CancellationToken cancellationToken)
    {
        var syntaxTask = GetSyntaxAsync(providers, context, cancellationToken);
        var decorationTask = GetDecorationsAsync(providers, context, cancellationToken);
        var tooltipTask = GetTooltipAsync(providers, context, cancellationToken);
        var completionTask = GetCompletionsAsync(providers, context, cancellationToken);
        var gutterTask = GetGutterAsync(providers, context, cancellationToken);

        await Task.WhenAll(syntaxTask, decorationTask, tooltipTask, completionTask, gutterTask)
            .ConfigureAwait(false);

        return new EditorProviderResults(
            requestId,
            context,
            FilterRanges(syntaxTask.Result, context.Snapshot),
            FilterRanges(decorationTask.Result, context.Snapshot),
            IsValidTooltip(tooltipTask.Result, context.Snapshot) ? tooltipTask.Result : null,
            NormalizeCompletion(completionTask.Result, context.Snapshot),
            gutterTask.Result.Where(item => item.Line < context.Snapshot.Lines.LineCount).ToArray());
    }

    private static SyntaxSpan[] FilterRanges(
        IReadOnlyList<SyntaxSpan> spans,
        TextSnapshot snapshot) =>
        spans.Where(span => IsValidRange(span.Range, snapshot)).ToArray();

    private static TextDecoration[] FilterRanges(
        IReadOnlyList<TextDecoration> decorations,
        TextSnapshot snapshot) =>
        decorations.Where(decoration => IsValidRange(decoration.Range, snapshot)).ToArray();

    private static bool IsValidTooltip(TooltipData? tooltip, TextSnapshot snapshot) =>
        tooltip is null || IsValidRange(tooltip.Range, snapshot);

    private static CompletionResult? NormalizeCompletion(
        CompletionResult? completion,
        TextSnapshot snapshot) => completion is null || IsValidRange(completion.ReplacementRange, snapshot)
            ? completion
            : null;

    private static bool IsValidRange(TextRange range, TextSnapshot snapshot) =>
        range.Start <= snapshot.Length && range.End <= snapshot.Length;

    private static Task<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderConfiguration providers,
        EditorProviderContext context,
        CancellationToken cancellationToken) =>
        providers.Syntax is null
            ? Task.FromResult<IReadOnlyList<SyntaxSpan>>(Array.Empty<SyntaxSpan>())
            : providers.Syntax.GetSyntaxAsync(context, cancellationToken).AsTask();

    private static Task<IReadOnlyList<TextDecoration>> GetDecorationsAsync(
        EditorProviderConfiguration providers,
        EditorProviderContext context,
        CancellationToken cancellationToken) =>
        providers.Decorations is null
            ? Task.FromResult<IReadOnlyList<TextDecoration>>(Array.Empty<TextDecoration>())
            : providers.Decorations.GetDecorationsAsync(context, cancellationToken).AsTask();

    private static Task<TooltipData?> GetTooltipAsync(
        EditorProviderConfiguration providers,
        EditorProviderContext context,
        CancellationToken cancellationToken) =>
        providers.Tooltip is null
            ? Task.FromResult<TooltipData?>(null)
            : providers.Tooltip.GetTooltipAsync(context, cancellationToken).AsTask();

    private static Task<CompletionResult?> GetCompletionsAsync(
        EditorProviderConfiguration providers,
        EditorProviderContext context,
        CancellationToken cancellationToken) =>
        providers.Completion is null
            ? Task.FromResult<CompletionResult?>(null)
            : providers.Completion.GetCompletionsAsync(context, cancellationToken).AsTask();

    private static Task<IReadOnlyList<GutterItem>> GetGutterAsync(
        EditorProviderConfiguration providers,
        EditorProviderContext context,
        CancellationToken cancellationToken) =>
        providers.Gutter is null
            ? Task.FromResult<IReadOnlyList<GutterItem>>(Array.Empty<GutterItem>())
            : providers.Gutter.GetGutterItemsAsync(context, cancellationToken).AsTask();

    private void CompleteRequest(RequestState request)
    {
        lock (_gate)
        {
            if (_activeCancellation == request.Source)
            {
                _activeCancellation = null;
            }
        }

        // A source is kept alive until its provider work has completed. This
        // avoids disposing a token source while a third-party provider is
        // still registering or observing its token.
        request.Source.Dispose();
    }

    private async Task CompleteWhenFinishedAsync(
        Task<EditorProviderResults> work,
        RequestState request)
    {
        try
        {
            await work.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The original request has already been superseded or canceled.
        }

        CompleteRequest(request);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void CancelSource(CancellationTokenSource? source)
    {
        if (source is not null)
        {
            // Cancellation callbacks belong to third-party providers. Run
            // them asynchronously so canceling a request cannot make a UI
            // input event wait for provider cleanup.
            _ = ObserveCancellationAsync(source.CancelAsync());
        }
    }

    private static async Task ObserveCancellationAsync(Task cancellation)
    {
        try
        {
            await cancellation.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Provider cancellation callbacks are isolated from the editor.
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
