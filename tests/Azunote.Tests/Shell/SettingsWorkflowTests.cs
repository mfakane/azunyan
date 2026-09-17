using Azunote;
using Xunit;

namespace Azunote.Tests.Shell;

public sealed class SettingsWorkflowTests
{
    [Fact]
    public async Task Initialize_loads_settings_and_builds_the_language_menu()
    {
        using var settings = new TemporarySettings();
        var editor = new FakeEditorView();
        var languageMenu = new FakeLanguageModeMenuView();
        var externalMenu = new FakeExternalToolMenuView();
        var prompt = new FakeUserPrompt();
        using var workflow = settings.CreateWorkflow(editor, languageMenu, externalMenu, prompt);

        await workflow.InitializeAsync();

        Assert.Empty(prompt.Errors);
        Assert.Equal(AzunoteSettings.DefaultFontFamily, editor.FontFamily);
        Assert.Equal(AzunoteSettings.DefaultFontSize, editor.FontSize);
        Assert.NotEmpty(languageMenu.Entries);
        Assert.Contains(externalMenu.Nodes, node => node.Name == "Format");
    }

    [Fact]
    public async Task Two_windows_share_one_reading_and_one_watch()
    {
        using var settings = new TemporarySettings();
        var firstMenu = new FakeExternalToolMenuView();
        var secondMenu = new FakeExternalToolMenuView();
        using var first = settings.CreateWorkflow(externalMenu: firstMenu);
        using var second = settings.CreateWorkflow(externalMenu: secondMenu);

        await first.InitializeAsync();
        await second.InitializeAsync();

        Assert.Contains(firstMenu.Nodes, node => node.Name == "Format");
        Assert.Contains(secondMenu.Nodes, node => node.Name == "Format");

        // The point of sharing: the folder is read once and watched once,
        // however many windows are showing it.
        Assert.Single(settings.Monitors.Monitors);
        Assert.Same(first.PreparedTools, second.PreparedTools);
    }

    [Fact]
    public async Task A_window_that_is_disposed_stops_hearing_about_changes()
    {
        using var settings = new TemporarySettings();
        var externalMenu = new FakeExternalToolMenuView();
        var workflow = settings.CreateWorkflow(externalMenu: externalMenu);
        await workflow.InitializeAsync();
        var renderCount = externalMenu.RenderCount;

        workflow.Dispose();
        await settings.ChangeAsync();

        Assert.Equal(renderCount, externalMenu.RenderCount);
    }

    private sealed class TemporarySettings : IDisposable
    {
        private readonly SettingsService _service;

        public TemporarySettings()
        {
            Directory = Path.Combine(Path.GetTempPath(), $"azunote-settings-{Guid.NewGuid():N}");
            _service = new SettingsService(new SettingsController(Directory), Monitors);
        }

        public string Directory { get; }

        public FakeFileChangeMonitorFactory Monitors { get; } = new();

        public SettingsWorkflow CreateWorkflow(
            FakeEditorView? editor = null,
            FakeLanguageModeMenuView? languageMenu = null,
            FakeExternalToolMenuView? externalMenu = null,
            FakeUserPrompt? prompt = null)
        {
            editor ??= new FakeEditorView();
            return new SettingsWorkflow(
                _service,
                new LanguageModeController(editor, languageMenu ?? new FakeLanguageModeMenuView(), () => null),
                externalMenu ?? new FakeExternalToolMenuView(),
                new FakeUiDispatcher(),
                prompt ?? new FakeUserPrompt(),
                new FakeSettingsFolderOpener(),
                () => null,
                _ => Task.CompletedTask);
        }

        /// <summary>Reports a change the way the watcher would, and waits for
        /// the reading it starts.</summary>
        public async Task ChangeAsync()
        {
            var reloaded = new TaskCompletionSource();
            void Loaded(object? sender, SettingsSnapshot snapshot) => reloaded.TrySetResult();
            _service.Loaded += Loaded;
            try
            {
                Assert.Single(Monitors.Monitors).Trigger(
                    SettingsFileService.GetSettingsFilePath(Directory));
                await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                _service.Loaded -= Loaded;
            }
        }

        public void Dispose()
        {
            _service.Dispose();
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}
