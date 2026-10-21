using System.Text;
using Azunote;
using Tomlyn;
using Xunit;

namespace Azunote.Tests.Settings;

public sealed class DefaultToolInstallerTests : IDisposable
{
    private const string SortTool = "Edit/SortSelectedLines.tool.toml";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"azunyan-default-tools-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task A_new_settings_directory_receives_every_default_tool()
    {
        await StartAsync();

        Assert.Contains("Sort-Object", await ReadToolAsync(SortTool), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_root, DefaultToolInstaller.RecordFileName)));
    }

    [Fact]
    public async Task An_untouched_tool_follows_the_bundle_and_a_changed_one_is_kept()
    {
        await StartAsync();
        var bundled = await ReadToolAsync(SortTool);
        const string ChangedPrettier = "name = \"My Prettier\"\n";
        await WriteToolAsync("Format/Prettier.tool.toml", ChangedPrettier);
        // The record as an earlier bundle would have left it.
        await WriteToolAsync(SortTool, "name = \"Old sort\"\n");
        await RecordOfferedAsync(SortTool, "name = \"Old sort\"\n");

        await StartAsync();

        Assert.Equal(bundled, await ReadToolAsync(SortTool));
        Assert.Equal(ChangedPrettier, await ReadToolAsync("Format/Prettier.tool.toml"));
    }

    [Fact]
    public async Task A_deleted_tool_stays_deleted()
    {
        await StartAsync();
        File.Delete(ToolPath(SortTool));

        await StartAsync();

        Assert.False(File.Exists(ToolPath(SortTool)));
    }

    [Fact]
    public async Task Without_a_record_earlier_bundled_versions_update_and_other_files_are_kept()
    {
        Directory.CreateDirectory(Path.Combine(_root, SettingsFileService.ToolsDirectoryName));
        await StartAsync();
        var bundled = await ReadToolAsync(SortTool);
        File.Delete(Path.Combine(_root, DefaultToolInstaller.RecordFileName));
        File.Delete(ToolPath("Edit/UrlEncode.tool.toml"));
        // The sort tool as it shipped when it also removed duplicates, with
        // the CRLF line endings a build may have given it.
        var earlierSort = bundled.Replace("'Sort-Object'", "'Sort-Object -Unique'", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal);
        await WriteToolAsync(SortTool, earlierSort);
        await WriteToolAsync("Format/Prettier.tool.toml", "name = \"My Prettier\"\n");

        await StartAsync();

        Assert.Equal(bundled, await ReadToolAsync(SortTool));
        Assert.True(File.Exists(ToolPath("Edit/UrlEncode.tool.toml")));
        Assert.Equal("name = \"My Prettier\"\n", await ReadToolAsync("Format/Prettier.tool.toml"));
    }

    private async Task StartAsync()
    {
        await SettingsFileService.EnsureExistsAsync(_root);
        await SettingsFileService.InstallDefaultToolsAsync(_root);
    }

    private string ToolPath(string relativePath) =>
        Path.Combine(_root, SettingsFileService.ToolsDirectoryName, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private Task<string> ReadToolAsync(string relativePath) => File.ReadAllTextAsync(ToolPath(relativePath));

    private Task WriteToolAsync(string relativePath, string text) => File.WriteAllTextAsync(ToolPath(relativePath), text);

    private async Task RecordOfferedAsync(string relativePath, string text)
    {
        var recordPath = Path.Combine(_root, DefaultToolInstaller.RecordFileName);
        var record = await SettingsFileService.DeserializeAsync(
            recordPath,
            AzunoteTomlSerializerContext.Default.DefaultToolsRecord,
            CancellationToken.None);
        record!.Offered[relativePath] = DefaultToolInstaller.Hash(Encoding.UTF8.GetBytes(text));
        await File.WriteAllTextAsync(
            recordPath,
            TomlSerializer.Serialize(record, AzunoteTomlSerializerContext.Default.DefaultToolsRecord));
    }
}
