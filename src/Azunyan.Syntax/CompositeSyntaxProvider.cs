using Azunyan.Core;

namespace Azunyan.Syntax;

/// <summary>
/// Combines syntax sources in priority order using a left-to-right lexical scan.
/// At each source position, the first source with a candidate wins and consumes
/// its complete range.
/// </summary>
public sealed class CompositeSyntaxProvider : IIncrementalSyntaxProvider
{
    private readonly IReadOnlyList<ISyntaxProvider> _sources;

    public CompositeSyntaxProvider(IEnumerable<ISyntaxProvider> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = [.. sources];
        if (_sources.Any(source => source is null))
        {
            throw new ArgumentException("Syntax sources cannot contain null.", nameof(sources));
        }
    }

    public IReadOnlyList<ISyntaxProvider> Sources => _sources;

    public async ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default) =>
        (await GetSyntaxAnalysisAsync(context, cancellationToken).ConfigureAwait(false)).Spans;

    public async ValueTask<SyntaxAnalysis> GetSyntaxAnalysisAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var tasks = _sources
            .Select(source => InvokeAsync(source, context, cancellationToken))
            .ToArray();
        var sourceResults = await Task.WhenAll(tasks).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Combine(sourceResults, context.Snapshot, cancellationToken);
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
        if (previousAnalysis.State is not CompositeSyntaxState state
            || state.Sources.Count != _sources.Count
            || change.OldRange.End > previousSnapshot.Length
            || change.NewRange.End > context.Snapshot.Length)
        {
            return await GetSyntaxAnalysisAsync(context, cancellationToken).ConfigureAwait(false);
        }

        var tasks = _sources
            .Select((source, index) => InvokeIncrementalAsync(
                source,
                state.Sources[index],
                context,
                previousSnapshot,
                change,
                cancellationToken))
            .ToArray();
        var sourceResults = await Task.WhenAll(tasks).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Combine(sourceResults, context.Snapshot, cancellationToken);
    }

    private static async Task<SyntaxAnalysis> InvokeAsync(
        ISyntaxProvider source,
        EditorProviderContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return source is ISyntaxAnalysisProvider analysisProvider
                ? await analysisProvider.GetSyntaxAnalysisAsync(context, cancellationToken).ConfigureAwait(false)
                : new(await source.GetSyntaxAsync(context, cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return SyntaxAnalysis.Empty;
        }
    }

    private static async Task<SyntaxAnalysis> InvokeIncrementalAsync(
        ISyntaxProvider source,
        SyntaxAnalysis previous,
        EditorProviderContext context,
        TextSnapshot previousSnapshot,
        TextChange change,
        CancellationToken cancellationToken)
    {
        try
        {
            return source is IIncrementalSyntaxProvider incremental
                ? await incremental.GetSyntaxAsync(
                    context,
                    previousSnapshot,
                    change,
                    previous,
                    cancellationToken).ConfigureAwait(false)
                : await InvokeAsync(source, context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return SyntaxAnalysis.Empty;
        }
    }

    private static SyntaxAnalysis Combine(
        IReadOnlyList<SyntaxAnalysis> sourceResults,
        TextSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var normalized = sourceResults
            .Select(result => result.Candidates
                .Select((span, order) => (Span: span, Order: order))
                .Where(item => !item.Span.Range.IsEmpty
                    && item.Span.Range.Start <= snapshot.Length
                    && item.Span.Range.End <= snapshot.Length)
                .OrderBy(item => item.Span.Range.Start)
                .ThenBy(item => item.Order)
                .ToArray())
            .ToArray();
        var indices = new int[normalized.Length];
        var result = new List<SyntaxSpan>();
        var position = 0;

        while (position < snapshot.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nextStart = snapshot.Length;
            SyntaxSpan? winner = null;
            for (var sourceIndex = 0; sourceIndex < normalized.Length; sourceIndex++)
            {
                var items = normalized[sourceIndex];
                while (indices[sourceIndex] < items.Length
                    && items[indices[sourceIndex]].Span.Range.Start < position)
                {
                    indices[sourceIndex]++;
                }

                if (indices[sourceIndex] >= items.Length)
                {
                    continue;
                }

                var candidate = items[indices[sourceIndex]].Span;
                nextStart = Math.Min(nextStart, candidate.Range.Start);
                if (candidate.Range.Start == position && winner is null)
                {
                    winner = candidate;
                }
            }

            if (winner is { } selected)
            {
                result.Add(selected);
                position = selected.Range.End;
            }
            else if (nextStart > position)
            {
                position = nextStart;
            }
            else
            {
                position++;
            }
        }

        return new SyntaxAnalysis(
            result,
            normalized.SelectMany(items => items.Select(item => item.Span)).ToArray(),
            new CompositeSyntaxState(sourceResults));
    }

    private sealed record CompositeSyntaxState(
        IReadOnlyList<SyntaxAnalysis> Sources) : SyntaxProviderState;
}
