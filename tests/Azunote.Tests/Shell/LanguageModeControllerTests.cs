using Azunote;
using Xunit;

namespace Azunote.Tests.Shell;

public sealed class LanguageModeControllerTests
{
    [Fact]
    public void Opened_path_selects_mode_and_applies_azunote_providers()
    {
        var editor = new FakeEditorView();
        var menu = new FakeLanguageModeMenuView();
        var controller = new LanguageModeController(
            editor,
            menu,
            () => "settings.toml");

        controller.Initialize();
        controller.DocumentOpened("settings.toml");

        Assert.Equal("azunote", controller.CurrentModeId);
        Assert.NotNull(editor.LanguageConfiguration?.Syntax);
        Assert.NotNull(editor.LanguageConfiguration?.Completion);
        Assert.Equal("azunote", menu.SelectedId);
    }

    [Fact]
    public void Manual_selection_prevents_later_path_auto_selection()
    {
        var editor = new FakeEditorView();
        var menu = new FakeLanguageModeMenuView();
        var controller = new LanguageModeController(
            editor,
            menu,
            () => "settings.toml");

        controller.Initialize();
        menu.SelectFromMenu("plain-text");
        controller.SelectForPath("settings.toml");

        Assert.Equal("plain-text", controller.CurrentModeId);
        Assert.True(controller.IsManuallySelected);
    }
}
