using System.Text.RegularExpressions;
using Azunote;
using Azunyan.Core;
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

    [Fact]
    public async Task Format_markdown_table_aligns_the_columns_from_its_script()
    {
        if (!PwshAvailable)
        {
            return;
        }

        var result = await RunAsync(
            "Format Markdown Table",
            "| Name | 数量 |\r\n|:--|--:|\r\n| apple | 10 |\r\n| りんご | 2 |");

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal(
            "| Name   | 数量 |\r\n| :----- | ---: |\r\n| apple  |   10 |\r\n| りんご |    2 |",
            result.StandardOutput);
    }

    [Fact]
    public async Task Calculate_answers_each_message()
    {
        if (!PwshAvailable)
        {
            return;
        }

        var result = await RunAsync(
            "Calculate",
            new ExternalToolContext(null, string.Empty, string.Empty)
                .WithPromptInput(new ExternalToolPromptInput("2 * (3 + 4)", "[]")));

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal("14", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task Complete_file_name_lists_the_document_folder()
    {
        if (!PwshAvailable)
        {
            return;
        }

        var folder = Directory.CreateDirectory(Path.Combine(_root, "doc"));
        folder.CreateSubdirectory("images");
        var document = Path.Combine(folder.FullName, "notes.md");
        await File.WriteAllTextAsync(document, string.Empty);

        var result = await RunAsync("Complete File Name", new ExternalToolContext(document, string.Empty, string.Empty));

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal(
            ["images/", "notes.md"],
            result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    [Theory]
    [InlineData("Run Batch File", "run me.cmd", "@echo off\r\necho ran %~nx0\r\n")]
    [InlineData("Run PowerShell Script", "run me.ps1", "\"ran $(Split-Path -Leaf $PSCommandPath)\"\r\n")]
    public async Task A_run_tool_runs_the_saved_script(string name, string fileName, string script)
    {
        if (!PwshAvailable)
        {
            return;
        }

        var folder = Directory.CreateDirectory(Path.Combine(_root, "scripts"));
        var path = Path.Combine(folder.FullName, fileName);
        await File.WriteAllTextAsync(path, script);

        var result = await RunAsync(name, new ExternalToolContext(path, script, string.Empty));

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal($"ran {fileName}", result.StandardOutput.Trim());
    }

    private Task<ExternalToolResult> RunAsync(string name, string selection) =>
        RunAsync(name, new ExternalToolContext(null, selection, selection));

    private async Task<ExternalToolResult> RunAsync(string name, ExternalToolContext context)
    {
        await SettingsFileService.EnsureExistsAsync(_root);
        await SettingsFileService.InstallDefaultFilesAsync(_root);
        var settings = await SettingsFileService.LoadAsync(_root);
        var tool = Assert.Single(settings.ExternalTools, tool => tool.Name == name);
        // The tool's folder is supplied as Azunote supplies it, for ${toolFolder}.
        return await ExternalToolRunner.RunAsync(
            tool.ToDefinition(),
            context with { ToolDirectory = tool.DefinitionDirectory });
    }
}
