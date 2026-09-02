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

    [Fact]
    public void Filters_tools_by_the_requested_menu_target()
    {
        var toolsOnly = CreateTool("Tools only");
        var contextOnly = CreateTool("Context only");
        contextOnly.Menus = ["context"];
        var nodes = new[]
        {
            new ExternalToolMenuNode("Tools", toolsOnly),
            new ExternalToolMenuNode("Context", contextOnly)
        };

        var entries = ExternalToolMenuBuilder.Build(
            nodes,
            _ => new ExternalToolMenuState(true, true),
            ExternalToolMenuTarget.Context);

        var entry = Assert.Single(entries);
        Assert.Same(contextOnly, entry.Tool);
    }

    [Fact]
    public void Flattens_context_tools_with_their_full_folder_path()
    {
        var tool = CreateTool("Format");
        tool.Menus = ["context"];
        var nodes = new[]
        {
            new ExternalToolMenuNode(
                "Formatting",
                children:
                [
                    new ExternalToolMenuNode(
                        "CSharp",
                        children: [new ExternalToolMenuNode(tool.Name, tool)])
                ])
        };

        var entries = ExternalToolMenuBuilder.BuildFlat(
            nodes,
            _ => new ExternalToolMenuState(true, true),
            ExternalToolMenuTarget.Context);

        var entry = Assert.Single(entries);
        Assert.Equal("Formatting: CSharp: Format", entry.Name);
        Assert.Same(tool, entry.Tool);
        Assert.Empty(entry.Children);
    }

    [Fact]
    public void A_tool_targeted_at_both_menus_is_present_in_both_builds()
    {
        var tool = CreateTool("Format");
        tool.Menus = ["tools", "context"];
        var nodes = new[] { new ExternalToolMenuNode(tool.Name, tool) };
        var getState = new Func<ExternalToolSettings, ExternalToolMenuState>(
            _ => new ExternalToolMenuState(true, true));

        Assert.Same(
            tool,
            Assert.Single(ExternalToolMenuBuilder.Build(
                nodes,
                getState,
                ExternalToolMenuTarget.Tools)).Tool);
        Assert.Same(
            tool,
            Assert.Single(ExternalToolMenuBuilder.BuildFlat(
                nodes,
                getState,
                ExternalToolMenuTarget.Context)).Tool);
    }

    private static ExternalToolSettings CreateTool(string name) => new()
    {
        Name = name
    };
}
