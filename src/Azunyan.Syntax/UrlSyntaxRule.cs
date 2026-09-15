using System.Text.RegularExpressions;
using Azunyan.Core;

namespace Azunyan.Syntax;

/// <summary>
/// Highlights URLs written in ordinary text. It recognizes the common
/// navigable schemes, stops before the punctuation that usually trails a URL
/// in prose, and keeps a balanced closing parenthesis that belongs to the URL.
/// A URL never spans a line, so an edit only rescans the lines it touched.
/// </summary>
public sealed class UrlSyntaxRule : IIncrementalSyntaxProvider
{
    // Characters that can never appear inside a URL written in prose. The CJK
    // punctuation and fullwidth blocks are excluded so a URL inside Japanese
    // text stops at 。、）「」 rather than swallowing them.
    private const string BodyCharacter =
        @"[^\s<>""'`^{}|\p{C}\p{IsCJKSymbolsandPunctuation}\p{IsHalfwidthandFullwidthForms}]";

    // The last character of a URL additionally excludes the punctuation that
    // normally ends the surrounding sentence or markup instead of the URL.
    private const string EndCharacter =
        @"[^\s<>""'`^{}|\p{C}\p{IsCJKSymbolsandPunctuation}\p{IsHalfwidthandFullwidthForms}.,;:!?*_~)\]}]";

    private static readonly Regex Pattern = new(
        @"(?<![A-Za-z0-9+.\-])(?:(?:https?|ftps?|file)://|mailto:)"
        + BodyCharacter + "*" + EndCharacter,
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public static string Classification => SyntaxClassifications.Link;

    public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default) =>
        GetSyntaxAsync(context, window: null, cancellationToken);

    public async ValueTask<SyntaxAnalysis> GetSyntaxAnalysisAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var spans = await GetSyntaxAsync(context, window: null, cancellationToken)
            .ConfigureAwait(false);
        return new SyntaxAnalysis(spans);
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
        if (change.OldRange.End > previousSnapshot.Length
            || change.NewRange.End > context.Snapshot.Length)
        {
            return await GetSyntaxAnalysisAsync(context, cancellationToken).ConfigureAwait(false);
        }

        var previousWindow = IncrementalSyntax.ExpandToLine(
            previousSnapshot,
            change.OldRange);
        var currentWindow = IncrementalSyntax.ExpandToLine(
            context.Snapshot,
            IncrementalSyntax.MapAfterChange(
                previousWindow,
                change,
                context.Snapshot.Length));
        var spans = await GetSyntaxAsync(context, currentWindow, cancellationToken)
            .ConfigureAwait(false);
        return IncrementalSyntax.Merge(
            previousAnalysis,
            change,
            previousWindow,
            currentWindow,
            spans);
    }

    private static ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        TextRange? window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var text = context.Snapshot.Text;
        var scanRange = window ?? TextRange.FromBounds(0, text.Length);
        var spans = new List<SyntaxSpan>();
        foreach (var match in Pattern.EnumerateMatches(
            text.AsSpan(scanRange.Start, scanRange.Length)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = scanRange.Start + match.Index;
            var end = ExtendBalancedParentheses(
                text,
                start,
                start + match.Length,
                scanRange.End);
            spans.Add(new SyntaxSpan(
                TextRange.FromBounds(start, end),
                Classification));
        }

        return ValueTask.FromResult<IReadOnlyList<SyntaxSpan>>(spans);
    }

    /// <summary>
    /// Restores a closing parenthesis that the trailing-punctuation rule
    /// removed from a URL such as https://example.com/a_(b).
    /// </summary>
    private static int ExtendBalancedParentheses(
        string text,
        int start,
        int end,
        int limit)
    {
        while (end < limit && text[end] == ')' && CountUnclosed(text, start, end) > 0)
        {
            end++;
        }

        return end;
    }

    private static int CountUnclosed(string text, int start, int end)
    {
        var depth = 0;
        for (var index = start; index < end; index++)
        {
            switch (text[index])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
            }
        }

        return depth;
    }
}
