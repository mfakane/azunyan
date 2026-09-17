using Azunote.Tests.Shell;
using Xunit;

namespace Azunote.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task The_folder_is_read_once_however_many_callers_ask_for_it()
    {
        using var directory = new TemporaryDirectory();
        var monitors = new FakeFileChangeMonitorFactory();
        using var service = new SettingsService(new SettingsController(directory.Path), monitors);
        var readings = 0;
        service.Loaded += (_, _) => Interlocked.Increment(ref readings);

        await Task.WhenAll(
            service.EnsureInitializedAsync(),
            service.EnsureInitializedAsync(),
            service.EnsureInitializedAsync());

        Assert.Equal(1, readings);
        Assert.NotNull(service.Current);
        Assert.Single(monitors.Monitors);
    }

    [Fact]
    public async Task The_watch_is_told_to_ignore_the_application_state_file()
    {
        using var directory = new TemporaryDirectory();
        var monitors = new FakeFileChangeMonitorFactory();
        using var service = new SettingsService(new SettingsController(directory.Path), monitors);

        await service.EnsureInitializedAsync();

        // The filter belongs to the monitor rather than to the handler: the
        // debounce window keeps one path, and a state write must not be the
        // one that survives a window a settings edit also entered.
        var monitor = Assert.Single(monitors.Monitors);
        Assert.NotNull(monitor.Ignore);
        var ignore = monitor.Ignore!;
        Assert.True(ignore(StateFileService.GetStateFilePath(directory.Path)));
        Assert.False(ignore(SettingsFileService.GetSettingsFilePath(directory.Path)));
        Assert.False(ignore(Path.Combine(
            directory.Path,
            SettingsFileService.ToolsDirectoryName,
            "Format.tool.toml")));
        Assert.False(ignore(null));
    }

    [Fact]
    public async Task A_change_publishes_a_new_reading()
    {
        using var directory = new TemporaryDirectory();
        var monitors = new FakeFileChangeMonitorFactory();
        using var service = new SettingsService(new SettingsController(directory.Path), monitors);
        await service.EnsureInitializedAsync();
        var first = service.Current;

        var reloaded = new TaskCompletionSource();
        service.Loaded += (_, _) => reloaded.TrySetResult();
        await File.WriteAllTextAsync(
            Path.Combine(
                SettingsFileService.GetToolsDirectoryPath(directory.Path),
                "Added.tool.toml"),
            """
            [launch]
            command = "cmd.exe"
            """);
        Assert.Single(monitors.Monitors).Trigger(
            Path.Combine(
                SettingsFileService.GetToolsDirectoryPath(directory.Path),
                "Added.tool.toml"));
        await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotSame(first, service.Current);
        Assert.Contains(service.Current!.Settings.ExternalTools, tool => tool.Name == "Added");
    }

    [Fact]
    public async Task A_service_that_was_disposed_reads_nothing_more()
    {
        using var directory = new TemporaryDirectory();
        var monitors = new FakeFileChangeMonitorFactory();
        var service = new SettingsService(new SettingsController(directory.Path), monitors);
        await service.EnsureInitializedAsync();
        var monitor = Assert.Single(monitors.Monitors);
        var readings = 0;
        service.Loaded += (_, _) => Interlocked.Increment(ref readings);

        service.Dispose();
        monitor.Trigger(SettingsFileService.GetSettingsFilePath(directory.Path));

        Assert.Equal(0, readings);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() =>
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"azunyan-service-{Guid.NewGuid():N}");

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
