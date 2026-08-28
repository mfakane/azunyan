using Xunit;

namespace Azunote.Tests.Settings;

public sealed class ApplicationStateTests
{
    [Fact]
    public async Task Ensure_exists_creates_a_default_state_toml()
    {
        var directory = CreateDirectory();
        try
        {
            await StateFileService.EnsureExistsAsync(directory);

            var path = StateFileService.GetStateFilePath(directory);
            Assert.True(File.Exists(path));
            var text = await File.ReadAllTextAsync(path);
            Assert.Contains("[window]", text, StringComparison.Ordinal);
            Assert.Contains("recentFiles", text, StringComparison.Ordinal);

            var state = await StateFileService.LoadAsync(directory);
            Assert.Equal(WindowLayoutState.DefaultWidth, state.Window.Width);
            Assert.Equal(WindowLayoutState.DefaultHeight, state.Window.Height);
            Assert.Empty(state.RecentFiles);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Controller_persists_window_size_and_deduplicated_recent_files()
    {
        var directory = CreateDirectory();
        try
        {
            using (var controller = new ApplicationStateController(directory))
            {
                controller.RecordRecentFile("first.txt");
                controller.RecordWindowSize(new WindowLayoutState
                {
                    Width = 1400,
                    Height = 900
                });
                await controller.InitializeAsync();
                controller.RecordRecentFile("second.txt");
                controller.RecordRecentFile("first.txt");
                await controller.FlushAsync();
            }

            var state = await StateFileService.LoadAsync(directory);
            Assert.Equal(1400, state.Window.Width);
            Assert.Equal(900, state.Window.Height);
            Assert.Equal(
                [Path.GetFullPath("first.txt"), Path.GetFullPath("second.txt")],
                state.RecentFiles);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task State_service_normalizes_invalid_window_dimensions()
    {
        var directory = CreateDirectory();
        try
        {
            await StateFileService.SaveAsync(
                directory,
                new AzunoteState
                {
                    Window = new WindowLayoutState
                    {
                        Width = 1,
                        Height = 1
                    }
                });

            var state = await StateFileService.LoadAsync(directory);
            Assert.Equal(WindowLayoutState.DefaultWidth, state.Window.Width);
            Assert.Equal(WindowLayoutState.DefaultHeight, state.Window.Height);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Controller_removes_a_recent_file_and_persists_the_change()
    {
        var directory = CreateDirectory();
        try
        {
            using (var controller = new ApplicationStateController(directory))
            {
                await controller.InitializeAsync();
                controller.RecordRecentFile("first.txt");
                controller.RecordRecentFile("second.txt");

                Assert.True(controller.RemoveRecentFile("first.txt"));
                Assert.False(controller.RemoveRecentFile("missing.txt"));
                await controller.FlushAsync();
            }

            var state = await StateFileService.LoadAsync(directory);
            Assert.Equal([Path.GetFullPath("second.txt")], state.RecentFiles);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static string CreateDirectory() =>
        Path.Combine(Path.GetTempPath(), $"azunote-state-{Guid.NewGuid():N}");

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
