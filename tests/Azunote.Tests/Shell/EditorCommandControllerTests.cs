using Azunote;
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
}
