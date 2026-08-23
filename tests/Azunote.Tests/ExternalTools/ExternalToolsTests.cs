using Xunit;

namespace Azunote.Tests;

public sealed class ExternalToolsTests
{
    [Fact]
    public void Command_line_parses_wait_position_and_path()
    {
        var options = AzunoteCommandLine.Parse(
            new[] { "--wait", "--line=12", "--column", "7", "notes.md" });

        Assert.True(options.WaitForExit);
        Assert.Equal(12, options.Line);
        Assert.Equal(7, options.Column);
        Assert.Equal("notes.md", options.FilePath);
        Assert.False(options.ReadStandardInput);
    }

    [Fact]
    public void Command_line_supports_external_editor_goto_and_stdin()
    {
        var gotoOptions = AzunoteCommandLine.Parse(new[] { "+4:9", "notes.md" });
        var stdinOptions = AzunoteCommandLine.Parse(new[] { "--stdin", "--line", "2" });

        Assert.Equal(4, gotoOptions.Line);
        Assert.Equal(9, gotoOptions.Column);
        Assert.True(stdinOptions.ReadStandardInput);
        Assert.Equal(2, stdinOptions.Line);
    }

    [Fact]
    public void Command_line_rejects_ambiguous_input_and_invalid_positions()
    {
        Assert.Throws<CommandLineParseException>(
            () => AzunoteCommandLine.Parse(new[] { "--stdin", "notes.md" }));
        Assert.Throws<CommandLineParseException>(
            () => AzunoteCommandLine.Parse(new[] { "--column=0" }));
    }

    [Fact]
    public void Context_exposes_file_values_and_expands_arguments()
    {
        var context = new ExternalToolContext(
            Path.Combine("folder", "notes.md"),
            "whole document",
            "selected text",
            lineNumber: 12,
            columnNumber: 7);

        Assert.Equal("notes.md", context.FileName);
        Assert.EndsWith("folder", context.FileDir, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            $"{context.FilePath}|{context.FileDir}|notes.md|whole document|selected text|12|7|{context.UserHome}",
            context.Expand("${file}|${fileDir}|${fileName}|${document}|${selection}|${lineNumber}|${columnNumber}|${userHome}"));
        Assert.Equal(
            Environment.GetEnvironmentVariable("PATH") ?? string.Empty,
            context.Expand("${env:PATH}"));
        Assert.Equal("selected text", context.GetInput(ExternalToolInputMode.Selection));
    }

    [Fact]
    public void Output_interpreter_maps_success_and_failure_without_mutating_text()
    {
        var replace = ExternalToolOutputInterpreter.Interpret(
            new ExternalToolDefinition(
                "formatter",
                outputMode: ExternalToolOutputMode.ReplaceSelection),
            new ExternalToolResult(0, "formatted", string.Empty));
        var failure = ExternalToolOutputInterpreter.Interpret(
            new ExternalToolDefinition("formatter"),
            new ExternalToolResult(2, string.Empty, "bad input"));

        Assert.True(replace.IsSuccess);
        Assert.Equal("formatted", replace.ReplacementText);
        Assert.False(replace.ReloadFile);
        Assert.False(failure.IsSuccess);
        Assert.Contains("bad input", failure.Error);
    }

    [Fact]
    public void Output_interpreter_can_request_reload_without_using_stdout()
    {
        var output = ExternalToolOutputInterpreter.Interpret(
            new ExternalToolDefinition(
                "prettier",
                outputMode: ExternalToolOutputMode.ReloadFile),
            new ExternalToolResult(0, "ignored", string.Empty));

        Assert.True(output.IsSuccess);
        Assert.True(output.ReloadFile);
        Assert.Null(output.ReplacementText);
    }

    [Fact]
    public async Task Runner_passes_document_on_stdin_and_captures_stdout()
    {
        var isWindows = OperatingSystem.IsWindows();
        var definition = new ExternalToolDefinition(
            isWindows ? "cmd.exe" : "/bin/cat",
            isWindows ? "/c more" : string.Empty,
            ExternalToolInputMode.Document);
        var result = await new ExternalToolRunner().RunAsync(
            definition,
            new ExternalToolContext(null, "stdin payload", string.Empty));

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal("stdin payload", result.StandardOutput.TrimEnd('\r', '\n'));
    }

    [Fact]
    public async Task Settings_service_loads_json5_comments_single_quotes_and_trailing_commas()
    {
        var root = Path.Combine(Path.GetTempPath(), $"azunyan-settings-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "settings.json5");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                {
                  // JSON5 permits comments and unquoted keys.
                  externalTools: [
                    {
                      name: 'Format document',
                      command: 'prettier',
                      arguments: '--write ${file}',
                      input: 'FilePath',
                      output: 'ReloadFile',
                    },
                  ],
                }
                """);

            var settings = await SettingsFileService.LoadAsync(path);
            var tool = Assert.Single(settings.ExternalTools);
            var definition = tool.ToDefinition();

            Assert.Equal("Format document", tool.Name);
            Assert.Equal("prettier", definition.FileName);
            Assert.Equal("--write ${file}", definition.Arguments);
            Assert.Equal(ExternalToolInputMode.FilePath, definition.InputMode);
            Assert.Equal(ExternalToolOutputMode.ReloadFile, definition.OutputMode);

            await SettingsFileService.SaveAsync(path, settings);
            var saved = await File.ReadAllTextAsync(path);
            Assert.Contains("externalTools", saved, StringComparison.Ordinal);
            Assert.Contains("Azunote settings", saved, StringComparison.Ordinal);
            Assert.Single((await SettingsFileService.LoadAsync(path)).ExternalTools);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Settings_service_creates_a_default_json5_file()
    {
        var root = Path.Combine(Path.GetTempPath(), $"azunyan-settings-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "settings.json5");
        try
        {
            await SettingsFileService.EnsureExistsAsync(path);

            Assert.True(File.Exists(path));
            var settings = await SettingsFileService.LoadAsync(path);
            Assert.Empty(settings.ExternalTools);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
