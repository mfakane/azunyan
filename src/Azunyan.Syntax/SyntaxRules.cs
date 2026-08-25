using System.Globalization;
using System.Text.RegularExpressions;
using Azunyan.Core;

namespace Azunyan.Syntax;

/// <summary>Highlights text between literal opening and closing tokens.</summary>
public sealed class DelimitedSyntaxRule : ISyntaxProvider
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
        GetSyntaxAsync(context, includeOverlappingCandidates: false, cancellationToken);

    internal ValueTask<IReadOnlyList<SyntaxSpan>> GetCandidatesAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken) =>
        GetSyntaxAsync(context, includeOverlappingCandidates: true, cancellationToken);

    private ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        bool includeOverlappingCandidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var text = context.Snapshot.Text;
        var spans = new List<SyntaxSpan>();
        var searchPosition = 0;

        while (searchPosition < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = text.IndexOf(OpeningToken, searchPosition, Comparison);
            if (start < 0)
            {
                break;
            }

            var position = start + OpeningToken.Length;
            while (position < text.Length)
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
public sealed class LineRemainderSyntaxRule : ISyntaxProvider
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
        GetSyntaxAsync(context, includeOverlappingCandidates: false, cancellationToken);

    internal ValueTask<IReadOnlyList<SyntaxSpan>> GetCandidatesAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken) =>
        GetSyntaxAsync(context, includeOverlappingCandidates: true, cancellationToken);

    private ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        bool includeOverlappingCandidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var text = context.Snapshot.Text;
        var spans = new List<SyntaxSpan>();
        var position = 0;
        while (position < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = text.IndexOf(Token, position, Comparison);
            if (start < 0)
            {
                break;
            }

            if (RequireLineStart && start > 0 && text[start - 1] is not '\r' and not '\n')
            {
                position = start + Token.Length;
                continue;
            }

            var end = start + Token.Length;
            while (end < text.Length && text[end] is not '\r' and not '\n')
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
public sealed class KeywordSyntaxRule : ISyntaxProvider
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
public sealed class LiteralSyntaxRule : ISyntaxProvider
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
    {
        ArgumentNullException.ThrowIfNull(context);
        var text = context.Snapshot.Text;
        var spans = new List<SyntaxSpan>();
        var position = 0;
        while (position <= text.Length - Token.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = text.IndexOf(Token, position, Comparison);
            if (start < 0)
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
public sealed class RegexSyntaxRule : ISyntaxProvider
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
}
