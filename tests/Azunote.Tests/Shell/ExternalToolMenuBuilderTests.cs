using Xunit;

namespace Azunote.Tests.Shell;

public sealed class ExternalToolMenuBuilderTests
{
    [Fact]
    public void Omits_a_folder_when_no_tool_inside_is_visible()
    {
        var hidden = CreateTool("Prettier");
        var nodes = new[]
        {
            new ExternalToolMenuNode(
                "Format",
                children: [new ExternalToolMenuNode(hidden.Name, hidden)])
        };

        var entries = ExternalToolMenuBuilder.Build(
            nodes,
            _ => new ExternalToolMenuState(false, false));

        Assert.Empty(entries);
    }

    [Fact]
    public void Promotes_the_only_visible_tool_to_its_folder_entry()
    {
        var hidden = CreateTool("ESLint");
        var visible = CreateTool("Prettier");
        var nodes = new[]
        {
            new ExternalToolMenuNode(
                "Format",
                children:
                [
                    new ExternalToolMenuNode(hidden.Name, hidden),
                    new ExternalToolMenuNode(visible.Name, visible)
                ])
        };

        var entries = ExternalToolMenuBuilder.Build(
            nodes,
            tool => tool == visible
                ? new ExternalToolMenuState(true, true)
                : new ExternalToolMenuState(false, false));

        var entry = Assert.Single(entries);
        Assert.Equal("Format: Prettier", entry.Name);
        Assert.Same(visible, entry.Tool);
        Assert.True(entry.State!.IsEnabled);
        Assert.Empty(entry.Children);
    }

    private static ExternalToolSettings CreateTool(string name) => new()
    {
        Name = name
    };
}
