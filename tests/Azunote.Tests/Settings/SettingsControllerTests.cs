using Xunit;

namespace Azunote.Tests;

public sealed class SettingsControllerTests
{
    [Fact]
    public async Task A_cancelled_load_does_not_become_the_current_settings()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"azunyan-settings-{Guid.NewGuid():N}");
        try
        {
            await SettingsFileService.EnsureExistsAsync(directory);
            var controller = new SettingsController(directory);
            var loaded = await controller.LoadAsync();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => controller.LoadAsync(cancellation.Token));

            Assert.Same(loaded, controller.Current);
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
