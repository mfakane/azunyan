using System.Text.RegularExpressions;
using Azunote;
using Xunit;

namespace Azunote.Tests.Settings;

/// <summary>Runs the default tools as Azunote would, on a selection.</summary>
public sealed class DefaultToolDefinitionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"azunyan-default-tool-runs-{Guid.NewGuid():N}");

    private static bool PwshAvailable =>
        OperatingSystem.IsWindows()
        && ExternalToolLaunchResolver.ResolvePowerShell() is not null;

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData("Sort Selected Lines", "b\r\na\r\nb", "a\r\nb\r\nb")]
    [InlineData("Reverse Selected Lines", "one\r\ntwo\r\nthree", "three\r\ntwo\r\none")]
    [InlineData("Join Lines", "  one  \r\n\r\n   two\r\nthree", "  one two three")]
    [InlineData("Convert to UPPERCASE", "Mixed\r\ncase", "MIXED\r\nCASE")]
    [InlineData("Convert to lowercase", "Mixed\r\nCASE", "mixed\r\ncase")]
    public async Task A_selection_tool_replaces_the_selection(string name, string selection, string expected)
    {
        if (!PwshAvailable)
        {
            return;
        }

        var result = await RunAsync(name, selection);

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal(expected, result.StandardOutput);
    }

    [Theory]
    [InlineData("Insert Date/Time", @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$")]
    [InlineData("Insert GUID", @"^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$")]
    public async Task An_insert_tool_writes_one_value(string name, string pattern)
    {
        if (!PwshAvailable)
        {
            return;
        }

        var result = await RunAsync(name, string.Empty);

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Matches(new Regex(pattern), result.StandardOutput);
    }

    [Theory]
    [InlineData("Check JSON", "{\n  \"a\": 1,\n  \"b\" 2\n}", 3)]
    [InlineData("Check XML", "<root>\n  <a></b>\n</root>", 2)]
    [InlineData("Check PowerShell Syntax", "$a = 1\nif ($a {\n}", 2)]
    public async Task A_check_tool_reports_where_the_text_is_invalid(string name, string text, int line)
    {
        if (!PwshAvailable)
        {
            return;
        }

        var result = await RunAsync(name, text);

        Assert.True(result.Succeeded, result.StandardError);
        var diagnostic = ExternalToolDiagnostics.Parse(result.StandardOutput).First();
        Assert.Equal(line, diagnostic.Line);
        Assert.True(ExternalToolDiagnostics.IsForDocument(diagnostic.File));
    }

    [Theory]
    [InlineData("Check JSON", "{ \"a\": [1, 2] }")]
    [InlineData("Check XML", "<root><a /></root>")]
    [InlineData("Check PowerShell Syntax", "$a = 1\nif ($a) { $a }")]
    public async Task A_check_tool_reports_nothing_for_valid_text(string name, string text)
    {
        if (!PwshAvailable)
        {
            return;
        }

        var result = await RunAsync(name, text);

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Empty(ExternalToolDiagnostics.Parse(result.StandardOutput));
    }

    private async Task<ExternalToolResult> RunAsync(string name, string selection)
    {
        await SettingsFileService.EnsureExistsAsync(_root);
        await SettingsFileService.InstallDefaultToolsAsync(_root);
        var settings = await SettingsFileService.LoadAsync(_root);
        var tool = Assert.Single(settings.ExternalTools, tool => tool.Name == name);
        return await ExternalToolRunner.RunAsync(
            tool.ToDefinition(),
            new ExternalToolContext(null, selection, selection));
    }
}
