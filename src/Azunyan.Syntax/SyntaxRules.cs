using System.Globalization;
using System.Text.RegularExpressions;
using Azunyan.Core;

namespace Azunyan.Syntax;

/// <summary>Highlights text between literal opening and closing tokens.</summary>
public sealed class DelimitedSyntaxRule : IIncrementalSyntaxProvider
{
    public DelimitedSyntaxRule(
        string openingToken,
        string closingToken,
        string classification,
        bool allowLineBreaks = true,
        string? escapePrefix = null,
        string? escapedEndToken = null,
        StringComparison comparison = StringComparison.Ordinal)
    {
        ArgumentException.ThrowIfNullOrEmpty(openingToken);
        ArgumentException.ThrowIfNullOrEmpty(closingToken);
        ArgumentException.ThrowIfNullOrEmpty(classification);
        if (escapePrefix is { Length: 0 })
        {
            throw new ArgumentException("An escape prefix cannot be empty.", nameof(escapePrefix));
        }

        if (escapedEndToken is { Length: 0 })
        {
            throw new ArgumentException("An escaped end token cannot be empty.", nameof(escapedEndToken));
        }

        OpeningToken = openingToken;
        ClosingToken = closingToken;
        Classification = classification;
        AllowLineBreaks = allowLineBreaks;
        EscapePrefix = escapePrefix;
        EscapedEndToken = escapedEndToken;
        Comparison = comparison;
    }

    public string OpeningToken { get; }

    public string ClosingToken { get; }

    public string Classification { get; }

    public bool AllowLineBreaks { get; }

    public string? EscapePrefix { get; }

    public string? EscapedEndToken { get; }

    public StringComparison Comparison { get; }

    public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default) =>
        GetSyntaxAsync(context, includeOverlappingCandidates: false, window: null, cancellationToken);

    public async ValueTask<SyntaxAnalysis> GetSyntaxAnalysisAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var spans = await GetSyntaxAsync(
            context,
            includeOverlappingCandidates: false,
            window: null,
            cancellationToken).ConfigureAwait(false);
        var candidates = await GetSyntaxAsync(
            context,
            includeOverlappingCandidates: true,
            window: null,
            cancellationToken).ConfigureAwait(false);
        return new SyntaxAnalysis(spans, candidates);
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

        var previousWindow = IncrementalSyntax.Expand(
            change.OldRange,
            OpeningToken.Length,
            ClosingToken.Length,
            previousSnapshot.Length);
        previousWindow = IncrementalSyntax.ExpandToPreviousSpan(
            previousAnalysis.Candidates,
            change.OldRange,
            previousWindow,
            previousSnapshot.Length);
        var currentWindow = IncrementalSyntax.MapAfterChange(
            previousWindow,
            change,
            context.Snapshot.Length);
        currentWindow = IncrementalSyntax.Expand(
            currentWindow,
            OpeningToken.Length,
            ClosingToken.Length,
            context.Snapshot.Length);

        var spans = await GetSyntaxAsync(
            context,
            includeOverlappingCandidates: false,
            window: currentWindow,
            cancellationToken).ConfigureAwait(false);
        var candidates = await GetSyntaxAsync(
            context,
            includeOverlappingCandidates: true,
            window: currentWindow,
            cancellationToken).ConfigureAwait(false);
        if (currentWindow.End < context.Snapshot.Length
            && (spans.Any(span => span.Range.End >= currentWindow.End)
                || candidates.Any(span => span.Range.End >= currentWindow.End)))
        {
            return await GetSyntaxAnalysisAsync(context, cancellationToken).ConfigureAwait(false);
        }

        return IncrementalSyntax.Merge(
            previousAnalysis,
            change,
            previousWindow,
            currentWindow,
            spans,
            candidates);
    }

    internal ValueTask<IReadOnlyList<SyntaxSpan>> GetCandidatesAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken) =>
        GetSyntaxAsync(context, includeOverlappingCandidates: true, window: null, cancellationToken);

    private ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        bool includeOverlappingCandidates,
        TextRange? window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var text = context.Snapshot.Text;
        var spans = new List<SyntaxSpan>();
        var scanRange = window ?? TextRange.FromBounds(0, text.Length);
        var searchPosition = scanRange.Start;

        while (searchPosition < scanRange.End)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = text.IndexOf(OpeningToken, searchPosition, Comparison);
            if (start < 0 || start >= scanRange.End)
            {
                break;
            }

            var position = start + OpeningToken.Length;
            while (position < scanRange.End)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!AllowLineBreaks && text[position] is '\r' or '\n')
                {
                    break;
                }

                if (EscapedEndToken is not null && StartsWith(text, position, EscapedEndToken))
                {
                    position += EscapedEndToken.Length;
                    continue;
                }

                if (EscapePrefix is not null && StartsWith(text, position, EscapePrefix))
                {
                    position += EscapePrefix.Length;
                    if (position < text.Length && (AllowLineBreaks || text[position] is not '\r' and not '\n'))
                    {
                        position++;
                    }

                    continue;
                }

                if (StartsWith(text, position, ClosingToken))
                {
                    position += ClosingToken.Length;
                    break;
                }

                position++;
            }

            spans.Add(new SyntaxSpan(TextRange.FromBounds(start, position), Classification));
            searchPosition = includeOverlappingCandidates
                ? start + OpeningToken.Length
                : Math.Max(position, start + OpeningToken.Length);
        }

        return ValueTask.FromResult<IReadOnlyList<SyntaxSpan>>(spans);
    }

    private bool StartsWith(string text, int position, string value) =>
        position <= text.Length - value.Length
        && text.AsSpan(position, value.Length).Equals(value.AsSpan(), Comparison);
}

/// <summary>Highlights from a literal token to, but not including, the line ending.</summary>
public sealed class LineRemainderSyntaxRule : IIncrementalSyntaxProvider
{
    public LineRemainderSyntaxRule(
        string token,
        string classification,
        bool requireLineStart = false,
        StringComparison comparison = StringComparison.Ordinal)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        ArgumentException.ThrowIfNullOrEmpty(classification);
        Token = token;
        Classification = classification;
        RequireLineStart = requireLineStart;
        Comparison = comparison;
    }

    public string Token { get; }

    public string Classification { get; }

    public bool RequireLineStart { get; }

    public StringComparison Comparison { get; }

    public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default) =>
        GetSyntaxAsync(context, includeOverlappingCandidates: false, window: null, cancellationToken);

    public async ValueTask<SyntaxAnalysis> GetSyntaxAnalysisAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var spans = await GetSyntaxAsync(
            context,
            includeOverlappingCandidates: false,
            window: null,
            cancellationToken).ConfigureAwait(false);
        var candidates = await GetSyntaxAsync(
            context,
            includeOverlappingCandidates: true,
            window: null,
            cancellationToken).ConfigureAwait(false);
        return new SyntaxAnalysis(spans, candidates);
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
        var spans = await GetSyntaxAsync(
            context,
            includeOverlappingCandidates: false,
            window: currentWindow,
            cancellationToken).ConfigureAwait(false);
        var candidates = await GetSyntaxAsync(
            context,
            includeOverlappingCandidates: true,
            window: currentWindow,
            cancellationToken).ConfigureAwait(false);
        return IncrementalSyntax.Merge(
            previousAnalysis,
            change,
            previousWindow,
            currentWindow,
            spans,
            candidates);
    }

    internal ValueTask<IReadOnlyList<SyntaxSpan>> GetCandidatesAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken) =>
        GetSyntaxAsync(context, includeOverlappingCandidates: true, window: null, cancellationToken);

    private ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        bool includeOverlappingCandidates,
        TextRange? window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var text = context.Snapshot.Text;
        var spans = new List<SyntaxSpan>();
        var scanRange = window ?? TextRange.FromBounds(0, text.Length);
        var position = scanRange.Start;
        while (position < scanRange.End)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = text.IndexOf(Token, position, Comparison);
            if (start < 0 || start >= scanRange.End)
            {
                break;
            }

            if (RequireLineStart && start > 0 && text[start - 1] is not '\r' and not '\n')
            {
                position = start + Token.Length;
                continue;
            }

            var end = start + Token.Length;
            while (end < scanRange.End && text[end] is not '\r' and not '\n')
            {
                end++;
            }

            spans.Add(new SyntaxSpan(TextRange.FromBounds(start, end), Classification));
            position = includeOverlappingCandidates
                ? start + Token.Length
                : Math.Max(end, start + Token.Length);
        }

        return ValueTask.FromResult<IReadOnlyList<SyntaxSpan>>(spans);
    }
}

/// <summary>Highlights identifier-like words from a fixed vocabulary.</summary>
public sealed class KeywordSyntaxRule : IIncrementalSyntaxProvider
{
    private readonly HashSet<string> _keywords;
    private readonly Func<char, bool> _isIdentifierPart;

    public KeywordSyntaxRule(
        IEnumerable<string> keywords,
        string classification = "keyword",
        StringComparer? comparer = null,
        Func<char, bool>? isIdentifierPart = null)
    {
        ArgumentNullException.ThrowIfNull(keywords);
        ArgumentException.ThrowIfNullOrEmpty(classification);
        _keywords = new HashSet<string>(keywords, comparer ?? StringComparer.Ordinal);
        if (_keywords.Count == 0 || _keywords.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException("At least one non-empty keyword is required.", nameof(keywords));
        }

        Classification = classification;
        _isIdentifierPart = isIdentifierPart ?? IsDefaultIdentifierPart;
    }

    public string Classification { get; }

    public IReadOnlySet<string> Keywords => _keywords;

    public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var text = context.Snapshot.Text;
        var spans = new List<SyntaxSpan>();
        var position = 0;
        while (position < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_isIdentifierPart(text[position]))
            {
                position++;
                continue;
            }

            var start = position++;
            while (position < text.Length && _isIdentifierPart(text[position]))
            {
                position++;
            }

            if (_keywords.Contains(text[start..position]))
            {
                spans.Add(new SyntaxSpan(TextRange.FromBounds(start, position), Classification));
            }
        }

        return ValueTask.FromResult<IReadOnlyList<SyntaxSpan>>(spans);
    }

    public async ValueTask<SyntaxAnalysis> GetSyntaxAnalysisAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var spans = await GetSyntaxAsync(
            context,
            window: null,
            cancellationToken).ConfigureAwait(false);
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

        var previousWindow = IncrementalSyntax.ExpandToIdentifier(
            previousSnapshot,
            change.OldRange,
            _isIdentifierPart);
        var currentWindow = IncrementalSyntax.ExpandToIdentifier(
            context.Snapshot,
            IncrementalSyntax.MapAfterChange(
                previousWindow,
                change,
                context.Snapshot.Length),
            _isIdentifierPart);
        var spans = await GetSyntaxAsync(
            context,
            currentWindow,
            cancellationToken).ConfigureAwait(false);
        return IncrementalSyntax.Merge(
            previousAnalysis,
            change,
            previousWindow,
            currentWindow,
            spans);
    }

    private ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        TextRange? window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var text = context.Snapshot.Text;
        var spans = new List<SyntaxSpan>();
        var scanRange = window ?? TextRange.FromBounds(0, text.Length);
        var position = scanRange.Start;
        while (position < scanRange.End)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_isIdentifierPart(text[position]))
            {
                position++;
                continue;
            }

            var start = position++;
            while (position < scanRange.End && _isIdentifierPart(text[position]))
            {
                position++;
            }

            if (_keywords.Contains(text[start..position]))
            {
                spans.Add(new SyntaxSpan(TextRange.FromBounds(start, position), Classification));
            }
        }

        return ValueTask.FromResult<IReadOnlyList<SyntaxSpan>>(spans);
    }

    private static bool IsDefaultIdentifierPart(char value)
    {
        if (char.IsLetterOrDigit(value) || char.IsSurrogate(value))
        {
            return true;
        }

        return char.GetUnicodeCategory(value) is
            UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.ConnectorPunctuation;
    }
}

/// <summary>Highlights every occurrence of a literal token.</summary>
public sealed class LiteralSyntaxRule : IIncrementalSyntaxProvider
{
    public LiteralSyntaxRule(
        string token,
        string classification,
        StringComparison comparison = StringComparison.Ordinal)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        ArgumentException.ThrowIfNullOrEmpty(classification);
        Token = token;
        Classification = classification;
        Comparison = comparison;
    }

    public string Token { get; }

    public string Classification { get; }

    public StringComparison Comparison { get; }

    public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
        => GetSyntaxAsync(context, window: null, cancellationToken);

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

        var previousWindow = IncrementalSyntax.Expand(
            change.OldRange,
            Token.Length,
            Token.Length,
            previousSnapshot.Length);
        var currentWindow = IncrementalSyntax.Expand(
            IncrementalSyntax.MapAfterChange(
                previousWindow,
                change,
                context.Snapshot.Length),
            Token.Length,
            Token.Length,
            context.Snapshot.Length);
        var spans = await GetSyntaxAsync(context, currentWindow, cancellationToken)
            .ConfigureAwait(false);
        return IncrementalSyntax.Merge(
            previousAnalysis,
            change,
            previousWindow,
            currentWindow,
            spans);
    }

    private ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        TextRange? window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var text = context.Snapshot.Text;
        var spans = new List<SyntaxSpan>();
        var scanRange = window ?? TextRange.FromBounds(0, text.Length);
        var position = scanRange.Start;
        while (position <= scanRange.End - Token.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = text.IndexOf(Token, position, Comparison);
            if (start < 0 || start > scanRange.End - Token.Length)
            {
                break;
            }

            spans.Add(new SyntaxSpan(new TextRange(start, Token.Length), Classification));
            position = start + Token.Length;
        }

        return ValueTask.FromResult<IReadOnlyList<SyntaxSpan>>(spans);
    }
}

/// <summary>Highlights non-empty matches of a regular expression.</summary>
public sealed class RegexSyntaxRule : IIncrementalSyntaxProvider
{
    private readonly Regex _regex;

    public RegexSyntaxRule(
        string pattern,
        string classification,
        RegexOptions options = RegexOptions.CultureInvariant,
        TimeSpan? matchTimeout = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        ArgumentException.ThrowIfNullOrEmpty(classification);
        if ((options & RegexOptions.Compiled) != 0)
        {
            throw new ArgumentException("Compiled regular expressions are not supported by this AOT-compatible rule.", nameof(options));
        }

        _regex = new Regex(pattern, options, matchTimeout ?? TimeSpan.FromSeconds(1));
        Classification = classification;
    }

    public string Classification { get; }

    public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var spans = new List<SyntaxSpan>();
        foreach (Match match in _regex.Matches(context.Snapshot.Text))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (match.Length == 0)
            {
                throw new InvalidOperationException("Syntax regular expressions must not produce zero-length matches.");
            }

            spans.Add(new SyntaxSpan(new TextRange(match.Index, match.Length), Classification));
        }

        return ValueTask.FromResult<IReadOnlyList<SyntaxSpan>>(spans);
    }

    public async ValueTask<SyntaxAnalysis> GetSyntaxAnalysisAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var spans = await GetSyntaxAsync(context, cancellationToken).ConfigureAwait(false);
        return new SyntaxAnalysis(spans);
    }

    public ValueTask<SyntaxAnalysis> GetSyntaxAsync(
        EditorProviderContext context,
        TextSnapshot previousSnapshot,
        TextChange change,
        SyntaxAnalysis previousAnalysis,
        CancellationToken cancellationToken = default)
    {
        // An arbitrary regular expression may depend on text far outside the
        // edit, so retain correctness by falling back to a complete scan.
        return GetSyntaxAnalysisAsync(context, cancellationToken);
    }
}
