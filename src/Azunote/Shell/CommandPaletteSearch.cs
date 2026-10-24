namespace Azunote;

/// <summary>
/// One entry of the command palette. <paramref name="Detail"/> is where the
/// entry lives, such as its menu or folder, and is searched together with the
/// title when the title alone does not match.
/// </summary>
internal sealed record CommandPaletteItem(
    string Title,
    string Detail,
    string? Shortcut,
    bool IsEnabled,
    bool IsChecked,
    Action Invoke,
    bool KeepsPaletteOpen = false);

internal sealed record CommandPaletteMatch(
    CommandPaletteItem Item,
    IReadOnlyList<int> TitlePositions);

internal static class CommandPaletteSearch
{
    private const int WordStartBonus = 8;
    private const int ConsecutiveBonus = 5;

    /// <summary>
    /// Returns the items matching <paramref name="query"/>, best first. Items
    /// whose title matches come before items that match only together with
    /// their detail; equal scores keep the given order.
    /// </summary>
    public static IReadOnlyList<CommandPaletteMatch> Filter(
        string query,
        IReadOnlyList<CommandPaletteItem> items)
    {
        if (query.Length == 0)
        {
            return items.Select(item => new CommandPaletteMatch(item, [])).ToArray();
        }

        var matches = new List<(CommandPaletteMatch Match, bool OnTitle, int Score)>();
        foreach (var item in items)
        {
            if (Match(query, item.Title) is { } titleMatch)
            {
                matches.Add((new CommandPaletteMatch(item, titleMatch.Positions), true, titleMatch.Score));
            }
            else if (Match(query, $"{item.Detail} {item.Title}") is { } searchMatch)
            {
                var titleStart = item.Detail.Length + 1;
                var positions = searchMatch.Positions
                    .Where(position => position >= titleStart)
                    .Select(position => position - titleStart)
                    .ToArray();
                matches.Add((new CommandPaletteMatch(item, positions), false, searchMatch.Score));
            }
        }

        return matches
            .OrderByDescending(match => match.OnTitle)
            .ThenByDescending(match => match.Score)
            .Select(match => match.Match)
            .ToArray();
    }

    /// <summary>
    /// Matches the characters of <paramref name="query"/> in order, ignoring
    /// case, and scores the placement that favors word starts and runs of
    /// consecutive characters. Returns null when the query is not a
    /// subsequence of the text.
    /// </summary>
    public static (int Score, IReadOnlyList<int> Positions)? Match(string query, string text)
    {
        if (query.Length == 0 || query.Length > text.Length)
        {
            return null;
        }

        // best[i, j]: the best score of query[..(i + 1)] with query[i] at text[j].
        var best = new int[query.Length, text.Length];
        var previous = new int[query.Length, text.Length];
        for (var i = 0; i < query.Length; i++)
        {
            // The best placement of query[i - 1] strictly before j - 1, which
            // can precede j without the consecutive bonus.
            var bestBefore = -1;
            var bestBeforeIndex = -1;
            for (var j = 0; j < text.Length; j++)
            {
                best[i, j] = -1;
                if (i > 0 && j >= 2 && best[i - 1, j - 2] > bestBefore)
                {
                    bestBefore = best[i - 1, j - 2];
                    bestBeforeIndex = j - 2;
                }

                if (char.ToUpperInvariant(query[i]) != char.ToUpperInvariant(text[j]))
                {
                    continue;
                }

                var score = 1 + (IsWordStart(text, j) ? WordStartBonus : 0);
                if (i == 0)
                {
                    best[i, j] = score;
                    continue;
                }

                var adjacent = j >= 1 ? best[i - 1, j - 1] : -1;
                if (adjacent >= 0 && adjacent + ConsecutiveBonus >= bestBefore)
                {
                    best[i, j] = score + adjacent + ConsecutiveBonus;
                    previous[i, j] = j - 1;
                }
                else if (bestBefore >= 0)
                {
                    best[i, j] = score + bestBefore;
                    previous[i, j] = bestBeforeIndex;
                }
            }
        }

        var last = query.Length - 1;
        var end = -1;
        for (var j = 0; j < text.Length; j++)
        {
            if (best[last, j] >= 0 && (end < 0 || best[last, j] > best[last, end]))
            {
                end = j;
            }
        }

        if (end < 0)
        {
            return null;
        }

        var positions = new int[query.Length];
        for (int i = last, j = end; i >= 0; i--)
        {
            positions[i] = j;
            j = previous[i, j];
        }

        // Among equal placements, a shorter text is the closer match.
        return (best[last, end] * 64 - Math.Min(text.Length, 63), positions);
    }

    private static bool IsWordStart(string text, int index)
    {
        if (index == 0)
        {
            return true;
        }

        var before = text[index - 1];
        var current = text[index];
        return !char.IsLetterOrDigit(before)
            || (char.IsLower(before) && char.IsUpper(current));
    }
}
