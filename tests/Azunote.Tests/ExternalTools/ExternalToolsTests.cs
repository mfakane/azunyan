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
            ExternalToolDefinition.ParseArguments(isWindows ? "/c more" : string.Empty),
            ExternalToolInputMode.Document);
        var result = await new ExternalToolRunner().RunAsync(
            definition,
            new ExternalToolContext(null, "stdin payload", string.Empty));

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal("stdin payload", result.StandardOutput.TrimEnd('\r', '\n'));
    }

    [Fact]
    public async Task Text_file_service_normalizes_lone_carriage_returns_when_saving()
    {
        var root = Path.Combine(Path.GetTempPath(), $"azunyan-text-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "document.txt");
        try
        {
            Directory.CreateDirectory(root);
            await TextFileService.WriteAsync(
                path,
                "first\rsecond\r\nthird",
                TextEncodingKind.Utf8);

            Assert.Equal(
                $"first{Environment.NewLine}second\r\nthird",
                await File.ReadAllTextAsync(path));
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
    public async Task Text_file_service_can_write_selected_encoding_and_line_ending()
    {
        var root = Path.Combine(Path.GetTempPath(), $"azunyan-text-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "document.txt");
        try
        {
            Directory.CreateDirectory(root);
            await TextFileService.WriteAsync(
                path,
                "first\r\nsecond\nthird",
                TextEncodingKind.Utf8Bom,
                LineEndingKind.Lf);

            var bytes = await File.ReadAllBytesAsync(path);
            Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
            var read = await TextFileService.ReadAsync(path);
            Assert.Equal("first\nsecond\nthird", read.Text);
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
    public async Task Settings_service_loads_toml_tools_and_builds_the_folder_hierarchy()
    {
        var root = Path.Combine(Path.GetTempPath(), $"azunyan-settings-{Guid.NewGuid():N}");
        var tools = Path.Combine(root, SettingsFileService.ToolsDirectoryName);
        var nestedDirectory = Path.Combine(tools, "Formatting", "CSharp");
        var toolPath = Path.Combine(nestedDirectory, "format.tool.toml");
        Directory.CreateDirectory(nestedDirectory);
        try
        {
            await File.WriteAllTextAsync(
                toolPath,
                """
                name = "Format document"
                command = "prettier"
                arguments = ["--write", "${file}"]
                input = "FilePath"
                output = "ReloadFile"
                """);

            var settings = await SettingsFileService.LoadAsync(root);
            var tool = Assert.Single(settings.ExternalTools);
            var definition = tool.ToDefinition();

            Assert.Equal("Format document", tool.Name);
            Assert.Equal("prettier", definition.FileName);
            Assert.Equal(["--write", "${file}"], definition.Arguments);
            Assert.Equal(ExternalToolInputMode.FilePath, definition.InputMode);
            Assert.Equal(ExternalToolOutputMode.ReloadFile, definition.OutputMode);

            var formatting = Assert.Single(settings.ExternalToolMenu);
            Assert.Equal("Formatting", formatting.Name);
            var csharp = Assert.Single(formatting.Children);
            Assert.Equal("CSharp", csharp.Name);
            Assert.Same(tool, Assert.Single(csharp.Children).Tool);
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
    public async Task Settings_service_treats_a_tool_bundle_directory_as_one_tool()
    {
        var root = Path.Combine(Path.GetTempPath(), $"azunyan-settings-{Guid.NewGuid():N}");
        var bundle = Path.Combine(
            root,
            SettingsFileService.ToolsDirectoryName,
            "Packages",
            "markdown.tool");
        try
        {
            Directory.CreateDirectory(bundle);
            await File.WriteAllTextAsync(
                Path.Combine(bundle, "manifest.toml"),
                """
                name = "Markdown preview"
                command = "markdown-preview"
                input = "Document"
                output = "NewDocument"
                """);

            var settings = await SettingsFileService.LoadAsync(root);

            var packages = Assert.Single(settings.ExternalToolMenu);
            var bundleNode = Assert.Single(packages.Children);
            Assert.Equal("Markdown preview", bundleNode.Name);
            Assert.True(bundleNode.IsTool);
            Assert.Equal("Markdown preview", Assert.Single(settings.ExternalTools).Name);
            Assert.EndsWith("markdown.tool", bundleNode.Tool!.DefinitionDirectory, StringComparison.OrdinalIgnoreCase);
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
    public async Task Settings_service_creates_a_toml_settings_file_and_tools_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"azunyan-settings-{Guid.NewGuid():N}");
        try
        {
            await SettingsFileService.EnsureExistsAsync(root);

            Assert.True(File.Exists(SettingsFileService.GetSettingsFilePath(root)));
            Assert.True(Directory.Exists(SettingsFileService.GetToolsDirectoryPath(root)));
            var settingsText = await File.ReadAllTextAsync(SettingsFileService.GetSettingsFilePath(root));
            Assert.Contains("TOML", settingsText, StringComparison.OrdinalIgnoreCase);
            var settings = await SettingsFileService.LoadAsync(root);
            Assert.Empty(settings.ExternalTools);
            await SettingsFileService.SaveAsync(root, settings);
            Assert.Contains("TOML", await File.ReadAllTextAsync(SettingsFileService.GetSettingsFilePath(root)), StringComparison.OrdinalIgnoreCase);
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
