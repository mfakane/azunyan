using Azunyan.Core;

namespace Azunyan.Syntax;

/// <summary>
/// Layers one syntax source on top of another. Unlike
/// <see cref="CompositeSyntaxProvider"/>, which lets the first source that
/// starts at a position consume its whole range, an overlay span replaces the
/// part of the base span it covers. That is what a URL inside a comment or a
/// string needs: the comment keeps its color around a link that keeps its own.
/// </summary>
public sealed class OverlaySyntaxProvider : IIncrementalSyntaxProvider
{
    private readonly ISyntaxProvider _baseProvider;
    private readonly ISyntaxProvider _overlay;

    public OverlaySyntaxProvider(ISyntaxProvider baseProvider, ISyntaxProvider overlay)
    {
        _baseProvider = baseProvider ?? throw new ArgumentNullException(nameof(baseProvider));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
    }

    public ISyntaxProvider Base => _baseProvider;

    public ISyntaxProvider Overlay => _overlay;

    public async ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default) =>
        (await GetSyntaxAnalysisAsync(context, cancellationToken).ConfigureAwait(false)).Spans;

    public async ValueTask<SyntaxAnalysis> GetSyntaxAnalysisAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var baseAnalysis = await AnalyzeAsync(_baseProvider, context, cancellationToken)
            .ConfigureAwait(false);
        var overlayAnalysis = await AnalyzeAsync(_overlay, context, cancellationToken)
            .ConfigureAwait(false);
        return Combine(baseAnalysis, overlayAnalysis);
    }

    public async ValueTask<SyntaxAnalysis> GetSyntaxAsync(
        EditorProviderContext context,
        TextSnapshot previousSnapshot,
        TextChange change,
        SyntaxAnalysis previousAnalysis,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(previousSnapshot);
        ArgumentNullException.ThrowIfNull(previousAnalysis);
        if (previousAnalysis.State is not OverlaySyntaxState state)
        {
            return await GetSyntaxAnalysisAsync(context, cancellationToken).ConfigureAwait(false);
        }

        var baseAnalysis = await AnalyzeIncrementalAsync(
            _baseProvider,
            state.Base,
            context,
            previousSnapshot,
            change,
            cancellationToken).ConfigureAwait(false);
        var overlayAnalysis = await AnalyzeIncrementalAsync(
            _overlay,
            state.Overlay,
            context,
            previousSnapshot,
            change,
            cancellationToken).ConfigureAwait(false);
        return Combine(baseAnalysis, overlayAnalysis);
    }

    private static SyntaxAnalysis Combine(
        SyntaxAnalysis baseAnalysis,
        SyntaxAnalysis overlayAnalysis)
    {
        var overlays = Flatten(overlayAnalysis.Spans);
        if (overlays.Count == 0)
        {
            return new SyntaxAnalysis(
                baseAnalysis.Spans,
                baseAnalysis.Candidates,
                new OverlaySyntaxState(baseAnalysis, overlayAnalysis));
        }

        var spans = new List<SyntaxSpan>(baseAnalysis.Spans.Count + overlays.Count);
        foreach (var span in baseAnalysis.Spans)
        {
            spans.AddRange(Subtract(span, overlays));
        }

        spans.AddRange(overlays);
        spans.Sort(static (left, right) => left.Range.Start.CompareTo(right.Range.Start));
        return new SyntaxAnalysis(
            spans,
            spans,
            new OverlaySyntaxState(baseAnalysis, overlayAnalysis));
    }

    /// <summary>
    /// Orders the overlay spans and drops the ones that overlap an earlier
    /// span, so the combined result stays free of overlapping ranges.
    /// </summary>
    private static List<SyntaxSpan> Flatten(IReadOnlyList<SyntaxSpan> spans)
    {
        var result = new List<SyntaxSpan>(spans.Count);
        foreach (var span in spans
            .Where(span => !span.Range.IsEmpty)
            .OrderBy(span => span.Range.Start)
            .ThenBy(span => span.Range.End))
        {
            if (result.Count == 0 || result[^1].Range.End <= span.Range.Start)
            {
                result.Add(span);
            }
        }

        return result;
    }

    private static IEnumerable<SyntaxSpan> Subtract(
        SyntaxSpan span,
        IReadOnlyList<SyntaxSpan> overlays)
    {
        var start = span.Range.Start;
        foreach (var overlay in overlays)
        {
            if (overlay.Range.End <= start)
            {
                continue;
            }

            if (overlay.Range.Start >= span.Range.End)
            {
                break;
            }

            if (overlay.Range.Start > start)
            {
                yield return new SyntaxSpan(
                    TextRange.FromBounds(start, overlay.Range.Start),
                    span.Classification);
            }

            start = overlay.Range.End;
        }

        if (start < span.Range.End)
        {
            yield return new SyntaxSpan(
                TextRange.FromBounds(start, span.Range.End),
                span.Classification);
        }
    }

    private static async ValueTask<SyntaxAnalysis> AnalyzeAsync(
        ISyntaxProvider provider,
        EditorProviderContext context,
        CancellationToken cancellationToken) =>
        provider is ISyntaxAnalysisProvider analysisProvider
            ? await analysisProvider.GetSyntaxAnalysisAsync(context, cancellationToken)
                .ConfigureAwait(false)
            : new(await provider.GetSyntaxAsync(context, cancellationToken).ConfigureAwait(false));

    private static async ValueTask<SyntaxAnalysis> AnalyzeIncrementalAsync(
        ISyntaxProvider provider,
        SyntaxAnalysis previous,
        EditorProviderContext context,
        TextSnapshot previousSnapshot,
        TextChange change,
        CancellationToken cancellationToken) =>
        provider is IIncrementalSyntaxProvider incremental
            ? await incremental.GetSyntaxAsync(
                context,
                previousSnapshot,
                change,
                previous,
                cancellationToken).ConfigureAwait(false)
            : await AnalyzeAsync(provider, context, cancellationToken).ConfigureAwait(false);

    private sealed record OverlaySyntaxState(
        SyntaxAnalysis Base,
        SyntaxAnalysis Overlay) : SyntaxProviderState;
}
