using System.Text;
using System.Text.RegularExpressions;

namespace Azunote;

public readonly record struct SearchMatch(int Start, int Length)
{
    public int End => checked(Start + Length);
}

public readonly record struct SearchResult(SearchMatch Match, bool Wrapped);

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
/// Selection and focus remain UI concerns.
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

    public static bool IsMatch(
        string text,
        string query,
        FindReplaceOptions options = FindReplaceOptions.None)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0)
        {
            return false;
        }

        if (options.HasFlag(FindReplaceOptions.RegularExpression))
        {
            var regex = CreateRegex(query, options);
            if (regex is null)
            {
                return false;
            }

            try
            {
                var match = regex.Match(text);
                return match.Success && match.Index == 0 && match.Length == text.Length;
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        return string.Equals(text, query, GetComparison(options));
    }

    public static SearchMatch? FindNext(
        string text,
        string query,
        int start,
        FindReplaceOptions options = FindReplaceOptions.None)
        => FindNextResult(text, query, start, options)?.Match;

    public static SearchResult? FindNextResult(
        string text,
        string query,
        int start,
        FindReplaceOptions options = FindReplaceOptions.None)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0)
        {
            return null;
        }

        var clampedStart = Math.Clamp(start, 0, text.Length);
        if (options.HasFlag(FindReplaceOptions.RegularExpression))
        {
            var regex = CreateRegex(query, options);
            if (regex is null)
            {
                return null;
            }

            try
            {
                var match = regex.Match(text, clampedStart);
                var regexWrapped = false;
                if (!match.Success && clampedStart > 0)
                {
                    match = regex.Match(text, 0);
                    regexWrapped = match.Success;
                }

                return match.Success
                    ? new SearchResult(
                        new SearchMatch(match.Index, match.Length),
                        regexWrapped)
                    : null;
            }
            catch (RegexMatchTimeoutException)
            {
                return null;
            }
        }

        var index = FindLiteralNext(text, query, clampedStart, options);
        var wrapped = false;
        if (index < 0 && clampedStart > 0)
        {
            index = FindLiteralNext(text, query, 0, options);
            wrapped = index >= 0;
        }

        return index < 0
            ? null
            : new SearchResult(new SearchMatch(index, query.Length), wrapped);
    }

    public static SearchMatch? FindPrevious(
        string text,
        string query,
        int endExclusive,
        FindReplaceOptions options = FindReplaceOptions.None)
        => FindPreviousResult(text, query, endExclusive, options)?.Match;

    public static SearchResult? FindPreviousResult(
        string text,
        string query,
        int endExclusive,
        FindReplaceOptions options = FindReplaceOptions.None)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0 || text.Length == 0)
        {
            return null;
        }

        var clampedEndExclusive = Math.Clamp(endExclusive, 0, text.Length);
        if (options.HasFlag(FindReplaceOptions.RegularExpression))
        {
            var regex = CreateRegex(query, options);
            if (regex is null)
            {
                return null;
            }

            try
            {
                var match = FindLastRegexMatch(regex, text, clampedEndExclusive);
                var regexWrapped = false;
                if (match is null)
                {
                    match = FindLastRegexMatch(regex, text, text.Length + 1);
                    regexWrapped = match is not null;
                }

                return match is { } found
                    ? new SearchResult(
                        new SearchMatch(found.Index, found.Length),
                        regexWrapped)
                    : null;
            }
            catch (RegexMatchTimeoutException)
            {
                return null;
            }
        }

        var index = FindLiteralPrevious(text, query, clampedEndExclusive, options);
        var wrapped = false;
        if (index < 0)
        {
            index = FindLiteralPrevious(text, query, text.Length, options);
            wrapped = index >= 0;
        }

        return index < 0
            ? null
            : new SearchResult(new SearchMatch(index, query.Length), wrapped);
    }

    public static int Count(
        string text,
        string query,
        FindReplaceOptions options = FindReplaceOptions.None)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0)
        {
            return 0;
        }

        if (options.HasFlag(FindReplaceOptions.RegularExpression))
        {
            var regex = CreateRegex(query, options);
            if (regex is null)
            {
                return 0;
            }

            try
            {
                return regex.Count(text);
            }
            catch (RegexMatchTimeoutException)
            {
                return 0;
            }
        }

        var count = 0;
        var position = 0;
        while ((position = FindLiteralNext(text, query, position, options)) >= 0)
        {
            count++;
            position += Math.Max(query.Length, 1);
        }

        return count;
    }

    public static int CountBefore(
        string text,
        string query,
        int start,
        FindReplaceOptions options = FindReplaceOptions.None)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0)
        {
            return 0;
        }

        var clampedStart = Math.Clamp(start, 0, text.Length);
        if (options.HasFlag(FindReplaceOptions.RegularExpression))
        {
            var regex = CreateRegex(query, options);
            if (regex is null)
            {
                return 0;
            }

            try
            {
                var count = 0;
                foreach (Match match in regex.Matches(text))
                {
                    if (match.Index >= clampedStart)
                    {
                        break;
                    }

                    count++;
                }

                return count;
            }
            catch (RegexMatchTimeoutException)
            {
                return 0;
            }
        }

        var literalCount = 0;
        var position = 0;
        var matchPosition = FindLiteralNext(text, query, position, options);
        while (matchPosition >= 0 && matchPosition < clampedStart)
        {
            literalCount++;
            position = matchPosition + Math.Max(query.Length, 1);
            matchPosition = FindLiteralNext(text, query, position, options);
        }

        return literalCount;
    }

    public static string ReplaceAll(
        string text,
        string query,
        string replacement,
        FindReplaceOptions options = FindReplaceOptions.None)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(replacement);
        if (query.Length == 0)
        {
            return text;
        }

        if (options.HasFlag(FindReplaceOptions.RegularExpression))
        {
            var regex = CreateRegex(query, options);
            if (regex is null)
            {
                return text;
            }

            try
            {
                return regex.Replace(text, replacement);
            }
            catch (RegexMatchTimeoutException)
            {
                return text;
            }
        }

        if (!options.HasFlag(FindReplaceOptions.MatchWholeWord))
        {
            return text.Replace(query, replacement, GetComparison(options));
        }

        var builder = new StringBuilder(text.Length);
        var copiedThrough = 0;
        var searchFrom = 0;
        var match = FindLiteralNext(text, query, searchFrom, options);
        while (match >= 0)
        {
            builder.Append(text, copiedThrough, match - copiedThrough);
            builder.Append(replacement);
            copiedThrough = match + query.Length;
            searchFrom = copiedThrough;
            match = FindLiteralNext(text, query, searchFrom, options);
        }

        builder.Append(text, copiedThrough, text.Length - copiedThrough);
        return builder.ToString();
    }

    public static string ReplaceMatch(
        string text,
        string query,
        string replacement,
        FindReplaceOptions options = FindReplaceOptions.None)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(replacement);
        if (!options.HasFlag(FindReplaceOptions.RegularExpression))
        {
            return replacement;
        }

        var regex = CreateRegex(query, options);
        if (regex is null)
        {
            return text;
        }

        try
        {
            return regex.Replace(text, replacement, 1);
        }
        catch (RegexMatchTimeoutException)
        {
            return text;
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

    private static int FindLiteralPrevious(
        string text,
        string query,
        int endExclusive,
        FindReplaceOptions options)
    {
        var comparison = GetComparison(options);
        var position = Math.Min(endExclusive, text.Length) - 1;
        while (position >= 0)
        {
            position = text.LastIndexOf(query, position, comparison);
            if (position < 0)
            {
                return -1;
            }

            if (!options.HasFlag(FindReplaceOptions.MatchWholeWord)
                || IsWholeWord(text, position, query.Length))
            {
                return position;
            }

            position--;
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

    private static Match? FindLastRegexMatch(Regex regex, string text, int endExclusive)
    {
        Match? last = null;
        foreach (Match match in regex.Matches(text))
        {
            if (match.Index >= endExclusive)
            {
                break;
            }

            last = match;
        }

        return last;
    }
}
