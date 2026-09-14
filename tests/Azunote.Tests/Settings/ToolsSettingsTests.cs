using Xunit;

namespace Azunote.Tests;

public sealed class ToolsSettingsTests
{
    private static async Task<AzunoteSettings> LoadAsync(string settingsToml)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"azunyan-tools-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, SettingsFileService.SettingsFileName),
                settingsToml);
            return await SettingsFileService.LoadAsync(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_tools_section_uses_the_derived_defaults()
    {
        var settings = await LoadAsync("fontSize = 14");

        Assert.Equal(1, settings.Tools.PowerShellWarmIdleProcesses);
        Assert.Equal(
            AzunoteToolsSettings.DefaultPowerShellWarmProcesses,
            settings.Tools.PowerShellWarmProcesses);
    }

    [Fact]
    public void The_default_maximum_follows_the_logical_processor_count()
    {
        // Reserved processes are warmed while the run's earlier parts are
        // executing, so the useful depth depends on how many cores are free.
        var expected = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

        Assert.Equal(expected, AzunoteToolsSettings.DefaultPowerShellWarmProcesses);
        Assert.InRange(AzunoteToolsSettings.DefaultPowerShellWarmProcesses, 1, 4);
        Assert.True(
            AzunoteToolsSettings.DefaultPowerShellWarmIdleProcesses
                <= AzunoteToolsSettings.DefaultPowerShellWarmProcesses,
            "The resting count must not exceed the maximum.");
    }

    [Fact]
    public async Task Tools_section_loads_the_warm_process_counts()
    {
        var settings = await LoadAsync(
            """
            [tools]
            powerShellWarmProcesses = 6
            powerShellWarmIdleProcesses = 2
            """);

        Assert.Equal(6, settings.Tools.PowerShellWarmProcesses);
        Assert.Equal(2, settings.Tools.PowerShellWarmIdleProcesses);
    }

    [Fact]
    public async Task A_zero_idle_count_keeps_no_process_waiting_between_runs()
    {
        var settings = await LoadAsync(
            """
            [tools]
            powerShellWarmProcesses = 4
            powerShellWarmIdleProcesses = 0
            """);

        Assert.Equal(0, settings.Tools.PowerShellWarmIdleProcesses);
    }

    [Theory]
    [InlineData("powerShellWarmProcesses = 0")]
    [InlineData("powerShellWarmProcesses = 17")]
    [InlineData("powerShellWarmIdleProcesses = -1")]
    public async Task Out_of_range_counts_are_rejected(string line)
    {
        var exception = await Assert.ThrowsAsync<SettingsFileException>(
            () => LoadAsync($"[tools]\n{line}\n"));

        Assert.Contains("tools.powerShell", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_idle_count_above_the_maximum_is_rejected()
    {
        var exception = await Assert.ThrowsAsync<SettingsFileException>(
            () => LoadAsync(
                """
                [tools]
                powerShellWarmProcesses = 2
                powerShellWarmIdleProcesses = 3
                """));

        Assert.Contains(
            "powerShellWarmIdleProcesses",
            exception.Message,
            StringComparison.Ordinal);
    }
}
