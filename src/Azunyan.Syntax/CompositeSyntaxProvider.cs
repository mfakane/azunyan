using Azunyan.Core;

namespace Azunyan.Syntax;

/// <summary>
/// Combines syntax sources in priority order using a left-to-right lexical scan.
/// At each source position, the first source with a candidate wins and consumes
/// its complete range.
/// </summary>
public sealed class CompositeSyntaxProvider : ISyntaxProvider
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var tasks = _sources.Select(source => InvokeAsync(source, context, cancellationToken)).ToArray();
        var sourceResults = await Task.WhenAll(tasks).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = sourceResults
            .Select(items => items
                .Select((span, order) => (Span: span, Order: order))
                .Where(item => !item.Span.Range.IsEmpty
                    && item.Span.Range.Start <= context.Snapshot.Length
                    && item.Span.Range.End <= context.Snapshot.Length)
                .OrderBy(item => item.Span.Range.Start)
                .ThenBy(item => item.Order)
                .ToArray())
            .ToArray();
        var indices = new int[normalized.Length];
        var result = new List<SyntaxSpan>();
        var position = 0;

        while (position < context.Snapshot.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nextStart = context.Snapshot.Length;
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

        return result;
    }

    private static async Task<IReadOnlyList<SyntaxSpan>> InvokeAsync(
        ISyntaxProvider source,
        EditorProviderContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var spans = source switch
            {
                DelimitedSyntaxRule delimited =>
                    await delimited.GetCandidatesAsync(context, cancellationToken).ConfigureAwait(false),
                LineRemainderSyntaxRule line =>
                    await line.GetCandidatesAsync(context, cancellationToken).ConfigureAwait(false),
                _ => await source.GetSyntaxAsync(context, cancellationToken).ConfigureAwait(false)
            };
            return spans ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }
    }
}
