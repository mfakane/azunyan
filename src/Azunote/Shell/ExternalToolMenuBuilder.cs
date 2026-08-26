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
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(getState);

        var entries = new List<ExternalToolMenuEntry>(nodes.Count);
        foreach (var node in nodes)
        {
            var entry = BuildNode(node, getState);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static ExternalToolMenuEntry? BuildNode(
        ExternalToolMenuNode node,
        Func<ExternalToolSettings, ExternalToolMenuState> getState)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Tool is { } tool)
        {
            var state = getState(tool);
            return state.IsVisible
                ? new ExternalToolMenuEntry(node.Name, tool, state, [])
                : null;
        }

        var children = Build(node.Children, getState);
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
}
