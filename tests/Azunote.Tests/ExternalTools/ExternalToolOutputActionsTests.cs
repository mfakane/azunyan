using Azunote;
using Xunit;

namespace Azunote.Tests.ExternalTools;

public sealed class ExternalToolOutputActionsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"azunote-actions-{Guid.NewGuid():N}.tool.toml");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public async Task New_document_can_name_the_language_mode_it_opens_in()
    {
        var settings = await ReadAsync("""stdout = ["newDocument:diff", "newDocument"]""");

        Assert.Equal(
            new ExternalToolOutputActions(ExternalToolOutputMode.NewDocument, ExternalToolOutputMode.NewDocument, "diff", null),
            settings.Launch.Stdout);
    }

    [Theory]
    [InlineData("replaceSelection:diff")]
    [InlineData("newDocument:")]
    public async Task Only_new_document_takes_a_language_mode(string action)
    {
        await Assert.ThrowsAnyAsync<Exception>(() => ReadAsync($"stdout = \"{action}\""));
    }

    private async Task<ExternalToolSettings> ReadAsync(string launchLine)
    {
        await File.WriteAllTextAsync(_path, $"name = \"t\"\n[launch]\ncommand = \"git\"\n{launchLine}\n");
        return (await SettingsFileService.DeserializeAsync(
            _path,
            AzunoteTomlSerializerContext.Default.ExternalToolSettings,
            CancellationToken.None))!;
    }
}
