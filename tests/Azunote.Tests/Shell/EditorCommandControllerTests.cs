using Azunote;
using Azunyan.Core;
using Xunit;

namespace Azunote.Tests.Shell;

public sealed class EditorCommandControllerTests
{
    [Fact]
    public void Word_wrap_and_status_bar_commands_update_the_view()
    {
        var editor = new FakeEditorView();
        var chrome = new FakeWindowChromeView();
        var commands = new EditorCommandController(editor, chrome);

        commands.ToggleWordWrap();
        commands.ToggleStatusBar();

        Assert.True(editor.WordWrapEnabled);
        Assert.True(chrome.WordWrapEnabled);
        Assert.False(chrome.IsStatusBarVisible);
    }

    [Fact]
    public void Tab_display_size_command_updates_the_editor_and_menu_state()
    {
        var editor = new FakeEditorView();
        var chrome = new FakeWindowChromeView();
        var commands = new EditorCommandController(editor, chrome);

        commands.SetTabDisplaySize(8);

        Assert.Equal(8, editor.TabDisplaySize);
        Assert.Equal(8, chrome.TabDisplaySize);
    }

    [Fact]
    public void Indent_size_command_updates_the_editor_and_menu_state()
    {
        var editor = new FakeEditorView();
        var chrome = new FakeWindowChromeView();
        var commands = new EditorCommandController(editor, chrome);

        commands.SetIndentSize(8);

        Assert.Equal(8, editor.IndentSize);
        Assert.Equal(8, chrome.IndentSize);

        commands.SetIndentSize(null);

        Assert.Null(editor.IndentSize);
        Assert.Null(chrome.IndentSize);
    }

    [Fact]
    public void Tab_input_mode_command_updates_the_editor_and_menu_state()
    {
        var editor = new FakeEditorView();
        var chrome = new FakeWindowChromeView();
        var commands = new EditorCommandController(editor, chrome);

        commands.SetIndentationInputMode(IndentationInputMode.Spaces);

        Assert.Equal(IndentationInputMode.Spaces, editor.IndentationInputMode);
        Assert.Equal(IndentationInputMode.Spaces, chrome.IndentationInputMode);

        commands.SetIndentationInputMode(IndentationInputMode.Auto);

        Assert.Equal(IndentationInputMode.Auto, editor.IndentationInputMode);
        Assert.Equal(IndentationInputMode.Auto, chrome.IndentationInputMode);
    }
}
