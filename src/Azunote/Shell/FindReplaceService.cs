using System.Text;
using System.Text.RegularExpressions;

namespace Azunote;

public readonly record struct SearchMatch(int Start, int Length)
{
    public int End => checked(Start + Length);
}

public readonly record struct SearchResult(SearchMatch Match, bool Wrapped);

/// <summary>
/// One replacement covering every match of a Replace All: <see cref="Range"/>
/// spans the first through the last match, and <see cref="Replacement"/> is
/// its new text.
/// </summary>
public readonly record struct ReplaceAllEdit(SearchMatch Range, string Replacement, int Count);

[Flags]
public enum FindReplaceOptions
{
    None = 0,
    MatchCase = 1,
    MatchWholeWord = 2,
    RegularExpression = 4
}

/// <summary>
/// Text-only find and replace operations used by the shell search panel.
/// Selection and focus remain UI concerns. Every operation sees the matches
/// that <see cref="FindAll"/> enumerates: non-overlapping, left to right, and
/// limited to an optional scope. A regular expression is matched against the
/// whole text even inside a scope, so lookarounds and anchors see the
/// surrounding text.
/// </summary>
public static class FindReplaceService
{
    private const StringComparison CaseInsensitiveComparison = StringComparison.CurrentCultureIgnoreCase;
    private const StringComparison CaseSensitiveComparison = StringComparison.CurrentCulture;

    public static bool IsValidQuery(
        string query,
        FindReplaceOptions options = FindReplaceOptions.None)
    {
        ArgumentNullException.ThrowIfNull(query);
        return !options.HasFlag(FindReplaceOptions.RegularExpression)
            || CreateRegex(query, options) is not null;
    }

    public static IReadOnlyList<SearchMatch> FindAll(
        string text,
        string query,
        FindReplaceOptions options = FindReplaceOptions.None,
        SearchMatch? scope = null) =>
        EnumerateMatches(text, query, options, scope)
            .Select(match => match.Match)
            .ToArray();

    /// <summary>
    /// Reports whether <paramref name="candidate"/> is exactly one of the
    /// matches of <paramref name="query"/> in <paramref name="text"/>.
    /// </summary>
    public static bool IsMatchAt(
        string text,
        string query,
        SearchMatch candidate,
        FindReplaceOptions options = FindReplaceOptions.None) =>
        EnumerateMatches(text, query, options, candidate)
            .Any(match => match.Match == candidate);

    public static SearchMatch? FindNext(
        string text,
        string query,
        int start,
        FindReplaceOptions options = FindReplaceOptions.None)
        => FindNextResult(text, query, start, options)?.Match;

    /// <summary>
    /// Finds the first match starting at or after <paramref name="start"/>,
    /// wrapping to the first match of the scope.
    /// </summary>
    public static SearchResult? FindNextResult(
        string text,
        string query,
        int start,
        FindReplaceOptions options = FindReplaceOptions.None,
        SearchMatch? scope = null)
    {
        var matches = FindAll(text, query, options, scope);
        if (matches.Count == 0)
        {
            return null;
        }

        foreach (var match in matches)
        {
            if (match.Start >= start)
            {
                return new SearchResult(match, false);
            }
        }

        return new SearchResult(matches[0], true);
    }

    public static SearchMatch? FindPrevious(
        string text,
        string query,
        int endExclusive,
        FindReplaceOptions options = FindReplaceOptions.None)
        => FindPreviousResult(text, query, endExclusive, options)?.Match;

    /// <summary>
    /// Finds the last match starting before <paramref name="endExclusive"/>,
    /// wrapping to the last match of the scope.
    /// </summary>
    public static SearchResult? FindPreviousResult(
        string text,
        string query,
        int endExclusive,
        FindReplaceOptions options = FindReplaceOptions.None,
        SearchMatch? scope = null)
    {
        var matches = FindAll(text, query, options, scope);
        if (matches.Count == 0)
        {
            return null;
        }

        for (var index = matches.Count - 1; index >= 0; index--)
        {
            if (matches[index].Start < endExclusive)
            {
                return new SearchResult(matches[index], false);
            }
        }

        return new SearchResult(matches[^1], true);
    }

    public static int Count(
        string text,
        string query,
        FindReplaceOptions options = FindReplaceOptions.None,
        SearchMatch? scope = null) =>
        FindAll(text, query, options, scope).Count;

    public static int CountBefore(
        string text,
        string query,
        int start,
        FindReplaceOptions options = FindReplaceOptions.None,
        SearchMatch? scope = null) =>
        FindAll(text, query, options, scope).Count(match => match.Start < start);

    /// <summary>
    /// Returns the text that replaces <paramref name="match"/>. A regular
    /// expression expands substitutions such as <c>$1</c> from the match in
    /// the context of the whole text.
    /// </summary>
    public static string GetReplacement(
        string text,
        string query,
        string replacement,
        SearchMatch match,
        FindReplaceOptions options = FindReplaceOptions.None)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        return EnumerateMatches(text, query, options, match)
            .Where(candidate => candidate.Match == match)
            .Select(candidate => candidate.Regex?.Result(replacement) ?? replacement)
            .FirstOrDefault(replacement);
    }

    /// <summary>
    /// Builds one replacement for every match in the scope, or returns
    /// <see langword="null"/> when there is none.
    /// </summary>
    public static ReplaceAllEdit? ReplaceAll(
        string text,
        string query,
        string replacement,
        FindReplaceOptions options = FindReplaceOptions.None,
        SearchMatch? scope = null)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var matches = EnumerateMatches(text, query, options, scope).ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        var start = matches[0].Match.Start;
        var builder = new StringBuilder();
        var copiedThrough = start;
        foreach (var (match, regexMatch) in matches)
        {
            builder.Append(text, copiedThrough, match.Start - copiedThrough);
            builder.Append(regexMatch?.Result(replacement) ?? replacement);
            copiedThrough = match.End;
        }

        return new ReplaceAllEdit(
            new SearchMatch(start, copiedThrough - start),
            builder.ToString(),
            matches.Length);
    }

    private static IEnumerable<(SearchMatch Match, Match? Regex)> EnumerateMatches(
        string text,
        string query,
        FindReplaceOptions options,
        SearchMatch? scope)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0)
        {
            yield break;
        }

        var start = Math.Clamp(scope?.Start ?? 0, 0, text.Length);
        var end = Math.Clamp(scope?.End ?? text.Length, start, text.Length);
        if (options.HasFlag(FindReplaceOptions.RegularExpression))
        {
            var regex = CreateRegex(query, options);
            if (regex is null)
            {
                yield break;
            }

            var results = new List<(SearchMatch, Match?)>();
            try
            {
                for (var match = regex.Match(text, start);
                    match.Success && match.Index <= end;
                    match = match.NextMatch())
                {
                    if (match.Index + match.Length <= end)
                    {
                        results.Add((new SearchMatch(match.Index, match.Length), match));
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                yield break;
            }

            foreach (var result in results)
            {
                yield return result;
            }

            yield break;
        }

        var position = start;
        while ((position = FindLiteralNext(text, query, position, options)) >= 0
            && position + query.Length <= end)
        {
            yield return (new SearchMatch(position, query.Length), null);
            position += query.Length;
        }
    }

    private static StringComparison GetComparison(FindReplaceOptions options) =>
        options.HasFlag(FindReplaceOptions.MatchCase)
            ? CaseSensitiveComparison
            : CaseInsensitiveComparison;

    private static int FindLiteralNext(
        string text,
        string query,
        int start,
        FindReplaceOptions options)
    {
        var comparison = GetComparison(options);
        var position = Math.Clamp(start, 0, text.Length);
        while ((position = text.IndexOf(query, position, comparison)) >= 0)
        {
            if (!options.HasFlag(FindReplaceOptions.MatchWholeWord)
                || IsWholeWord(text, position, query.Length))
            {
                return position;
            }

            position += Math.Max(query.Length, 1);
        }

        return -1;
    }

    private static bool IsWholeWord(string text, int start, int length)
    {
        var end = checked(start + length);
        return (start == 0 || !IsWordCharacter(text[start - 1]))
            && (end == text.Length || !IsWordCharacter(text[end]));
    }

    private static bool IsWordCharacter(char value) =>
        char.IsLetterOrDigit(value) || value == '_';

    private static Regex? CreateRegex(string query, FindReplaceOptions options)
    {
        var pattern = options.HasFlag(FindReplaceOptions.MatchWholeWord)
            ? $"\\b(?:{query})\\b"
            : query;
        var regexOptions = options.HasFlag(FindReplaceOptions.MatchCase)
            ? RegexOptions.None
            : RegexOptions.IgnoreCase;
        try
        {
            return new Regex(pattern, regexOptions);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
