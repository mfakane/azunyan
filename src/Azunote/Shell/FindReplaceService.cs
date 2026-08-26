namespace Azunote;

public readonly record struct SearchMatch(int Start, int Length)
{
    public int End => checked(Start + Length);
}

/// <summary>
/// Text-only find and replace operations used by the shell search panel.
/// Selection and focus remain UI concerns.
/// </summary>
public static class FindReplaceService
{
    private const StringComparison Comparison = StringComparison.CurrentCultureIgnoreCase;

    public static bool IsMatch(string text, string query)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        return string.Equals(text, query, Comparison);
    }

    public static SearchMatch? FindNext(string text, string query, int start)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0)
        {
            return null;
        }

        var clampedStart = Math.Clamp(start, 0, text.Length);
        var index = text.IndexOf(query, clampedStart, Comparison);
        if (index < 0 && clampedStart > 0)
        {
            index = text.IndexOf(query, 0, Comparison);
        }

        return index < 0 ? null : new SearchMatch(index, query.Length);
    }

    public static int Count(string text, string query)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0)
        {
            return 0;
        }

        var count = 0;
        var position = 0;
        while ((position = text.IndexOf(query, position, Comparison)) >= 0)
        {
            count++;
            position += query.Length;
        }

        return count;
    }

    public static string ReplaceAll(string text, string query, string replacement)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(replacement);
        return query.Length == 0
            ? text
            : text.Replace(query, replacement, Comparison);
    }
}
