namespace Azunote;

internal sealed record ExternalToolMenuEntry(
    string Name,
    ExternalToolSettings? Tool,
    ExternalToolMenuState? State,
    IReadOnlyList<ExternalToolMenuEntry> Children)
{
    public bool IsTool => Tool is not null;
}

internal static class ExternalToolMenuBuilder
{
    public static IReadOnlyList<ExternalToolMenuEntry> Build(
        IReadOnlyList<ExternalToolMenuNode> nodes,
        Func<ExternalToolSettings, ExternalToolMenuState> getState)
        => Build(nodes, getState, ExternalToolMenuTarget.Tools);

    public static IReadOnlyList<ExternalToolMenuEntry> Build(
        IReadOnlyList<ExternalToolMenuNode> nodes,
        Func<ExternalToolSettings, ExternalToolMenuState> getState,
        ExternalToolMenuTarget target)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(getState);

        var entries = new List<ExternalToolMenuEntry>(nodes.Count);
        foreach (var node in nodes)
        {
            var entry = BuildNode(node, getState, target);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    public static IReadOnlyList<ExternalToolMenuEntry> BuildFlat(
        IReadOnlyList<ExternalToolMenuNode> nodes,
        Func<ExternalToolSettings, ExternalToolMenuState> getState,
        ExternalToolMenuTarget target)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(getState);

        var entries = new List<ExternalToolMenuEntry>();
        foreach (var node in nodes)
        {
            AddFlatEntries(node, [], getState, target, entries);
        }

        return entries;
    }

    public static bool NeedsContextSeparator(string? previousName, string name) =>
        previousName is not null && TopFolder(previousName) != TopFolder(name);

    public static string TopFolder(string name)
    {
        var split = name.Split(": ", 2, StringSplitOptions.None);
        return split.Length == 2 ? split[0] : string.Empty;
    }

    public static ExternalToolSettings? SelectByPriority(
        IReadOnlyList<ExternalToolSettings> candidates,
        Func<ExternalToolSettings, bool> isEnabled)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(isEnabled);

        ExternalToolSettings? selected = null;
        foreach (var candidate in candidates)
        {
            if (!isEnabled(candidate))
            {
                continue;
            }

            if (selected is null || candidate.Priority > selected.Priority)
            {
                selected = candidate;
            }
        }

        return selected;
    }

    public static IEnumerable<ExternalToolSettings> EnumerateTools(
        IReadOnlyList<ExternalToolMenuNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        foreach (var node in nodes)
        {
            foreach (var tool in EnumerateTools(node))
            {
                yield return tool;
            }
        }
    }

    private static ExternalToolMenuEntry? BuildNode(
        ExternalToolMenuNode node,
        Func<ExternalToolSettings, ExternalToolMenuState> getState,
        ExternalToolMenuTarget target)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Tool is { } tool)
        {
            if (!tool.IsShownIn(target))
            {
                return null;
            }

            var state = getState(tool);
            return state.IsVisible
                ? new ExternalToolMenuEntry(node.Name, tool, state, [])
                : null;
        }

        var children = Build(node.Children, getState, target);
        if (children.Count == 0)
        {
            return null;
        }

        if (children.Count == 1 && children[0].Tool is { } childTool)
        {
            var child = children[0];
            return new ExternalToolMenuEntry(
                $"{node.Name}: {child.Name}",
                childTool,
                child.State,
                []);
        }

        return new ExternalToolMenuEntry(node.Name, null, null, children);
    }

    private static void AddFlatEntries(
        ExternalToolMenuNode node,
        string[] parentPath,
        Func<ExternalToolSettings, ExternalToolMenuState> getState,
        ExternalToolMenuTarget target,
        ICollection<ExternalToolMenuEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Tool is { } tool)
        {
            if (!tool.IsShownIn(target))
            {
                return;
            }

            var state = getState(tool);
            if (state.IsVisible)
            {
                var path = parentPath.Length == 0
                    ? node.Name
                    : string.Join(": ", parentPath.Append(node.Name));
                entries.Add(new ExternalToolMenuEntry(path, tool, state, []));
            }

            return;
        }

        var nextPath = parentPath.Append(node.Name).ToArray();
        foreach (var child in node.Children)
        {
            AddFlatEntries(child, nextPath, getState, target, entries);
        }
    }

    private static IEnumerable<ExternalToolSettings> EnumerateTools(
        ExternalToolMenuNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Tool is { } tool)
        {
            yield return tool;
            yield break;
        }

        foreach (var child in node.Children)
        {
            foreach (var childTool in EnumerateTools(child))
            {
                yield return childTool;
            }
        }
    }
}
