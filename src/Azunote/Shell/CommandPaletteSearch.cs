using Azunyan.Core;

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
    /// Matches <paramref name="query"/> as <see cref="FuzzyMatch.Match"/>
    /// does, preferring a shorter text among equal placements.
    /// </summary>
    public static (int Score, IReadOnlyList<int> Positions)? Match(string query, string text) =>
        FuzzyMatch.Match(query, text) is { } match
            ? (match.Score * 64 - Math.Min(text.Length, 63), match.Positions)
            : null;
}
