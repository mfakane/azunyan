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

    [Fact]
    public async Task The_watcher_is_told_to_ignore_the_application_state_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"azunote-settings-{Guid.NewGuid():N}");
        try
        {
            var editor = new FakeEditorView();
            var factory = new FakeFileChangeMonitorFactory();
            var workflow = new SettingsWorkflow(
                new SettingsController(directory),
                new LanguageModeController(editor, new FakeLanguageModeMenuView(), () => null),
                new FakeExternalToolMenuView(),
                factory,
                new FakeUiDispatcher(),
                new FakeUserPrompt(),
                new FakeSettingsFolderOpener(),
                () => null,
                _ => Task.CompletedTask);

            await workflow.InitializeAsync();

            // The filter belongs to the monitor rather than to the handler:
            // the debounce window keeps one path, and a state write must not
            // be the one that survives a window a settings edit also entered.
            var monitor = Assert.Single(factory.Monitors);
            Assert.NotNull(monitor.Ignore);
            var ignore = monitor.Ignore!;
            Assert.True(ignore(StateFileService.GetStateFilePath(directory)));
            Assert.False(ignore(SettingsFileService.GetSettingsFilePath(directory)));
            Assert.False(ignore(Path.Combine(
                directory,
                SettingsFileService.ToolsDirectoryName,
                "Format.tool.toml")));
            Assert.False(ignore(null));
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
