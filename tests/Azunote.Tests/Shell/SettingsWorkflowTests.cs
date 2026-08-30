using Azunote;
using Xunit;

namespace Azunote.Tests.Shell;

public sealed class SettingsWorkflowTests
{
    [Fact]
    public async Task Initialize_loads_settings_and_builds_the_language_menu()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"azunote-settings-{Guid.NewGuid():N}");
        try
        {
            var editor = new FakeEditorView();
            var languageMenu = new FakeLanguageModeMenuView();
            var modes = new LanguageModeController(editor, languageMenu, () => null);
            var externalMenu = new FakeExternalToolMenuView();
            var prompt = new FakeUserPrompt();
            var workflow = new SettingsWorkflow(
                new SettingsController(directory),
                modes,
                externalMenu,
                new FakeFileChangeMonitorFactory(),
                new FakeUiDispatcher(),
                prompt,
                new FakeSettingsFolderOpener(),
                () => null,
                _ => Task.CompletedTask);

            await workflow.InitializeAsync();

            Assert.Empty(prompt.Errors);
            Assert.Equal(AzunoteSettings.DefaultFontFamily, editor.FontFamily);
            Assert.Equal(AzunoteSettings.DefaultFontSize, editor.FontSize);
            Assert.NotEmpty(languageMenu.Entries);
            Assert.Contains(externalMenu.Nodes, node => node.Name == "Format");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
