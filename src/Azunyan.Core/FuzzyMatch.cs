namespace Azunyan.Core;

/// <summary>
/// Matches typed characters against a name in order, skipping the characters
/// between them, as the command palette and completion narrow their lists.
/// </summary>
public static class FuzzyMatch
{
    private const int WordStartBonus = 8;
    private const int ConsecutiveBonus = 5;

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

        return (best[last, end], positions);
    }

    /// <summary>
    /// Whether a word starts at <paramref name="index"/>: after a character
    /// other than a letter or digit, or at a lower-to-upper case change.
    /// </summary>
    public static bool IsWordStart(string text, int index)
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

    /// <summary>
    /// Scores the word typed for a completion against a candidate's
    /// <paramref name="text"/>. Its first character has to match where a word
    /// starts, so <c>wl</c> finds <c>WriteLine</c> but <c>rite</c> does not
    /// find <c>Write</c>. An empty word matches every candidate equally.
    /// </summary>
    public static int? MatchCompletion(string word, string text)
    {
        if (word.Length == 0)
        {
            return 0;
        }

        return Match(word, text) is { } match && IsWordStart(text, match.Positions[0])
            ? match.Score
            : null;
    }
}
