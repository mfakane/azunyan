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
    public void Command_line_detects_help_before_the_end_of_options()
    {
        Assert.True(AzunoteCommandLine.IsHelpRequested(new[] { "notes.md", "--help" }));
        Assert.True(AzunoteCommandLine.IsHelpRequested(new[] { "-h" }));
        Assert.False(AzunoteCommandLine.IsHelpRequested(new[] { "--", "--help" }));
    }

    [Fact]
    public void Command_line_writes_help_for_the_supported_options()
    {
        using var writer = new StringWriter();

        AzunoteCommandLine.WriteUsage(writer);

        var output = writer.ToString();
        Assert.Equal(AzunoteCommandLine.Usage + Environment.NewLine, output);
        Assert.Contains("-h, --help", output, StringComparison.Ordinal);
        Assert.Contains("-w, --wait", output, StringComparison.Ordinal);
        Assert.Contains("-l, --line N", output, StringComparison.Ordinal);
        Assert.Contains("-c, --column N", output, StringComparison.Ordinal);
        Assert.Contains("--stdin, -", output, StringComparison.Ordinal);
        Assert.Contains("+N[:M]", output, StringComparison.Ordinal);
        Assert.Contains("--", output, StringComparison.Ordinal);
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
            $"{context.FilePath}|{context.FileDir}|notes.md|whole document|selected text|12|7|{ExternalToolContext.UserHome}",
            context.Expand("${file}|${fileDir}|${fileName}|${document}|${selection}|${lineNumber}|${columnNumber}|${userHome}"));
        Assert.Equal(
            Environment.GetEnvironmentVariable("PATH") ?? string.Empty,
            context.Expand("${env:PATH}"));
        Assert.Equal("selected text", context.GetInput(ExternalToolInputMode.Selection));
    }

    [Fact]
    public void Context_distinguishes_document_and_execution_files()
    {
        var documentPath = Path.Combine("folder", "notes.md");
        var temporaryPath = Path.Combine(Path.GetTempPath(), "azunote-external-notes.md");
        var context = new ExternalToolContext(
            documentPath,
            temporaryPath,
            "document",
            string.Empty,
            languageId: "markdown",
            toolDirectory: Path.Combine("tools", "Format"),
            encoding: TextEncodingKind.Utf8Bom,
            lineEnding: LineEndingKind.CrLf,
            isDirty: true);

        Assert.Equal(Path.GetFullPath(temporaryPath), context.FilePath);
        Assert.Equal(Path.GetFullPath(documentPath), context.DocumentFilePath);
        Assert.Equal(Path.GetFullPath(temporaryPath), context.TempFile);
        Assert.Equal("notes.md", context.Expand("${documentName}"));
        Assert.Equal("markdown|Utf8Bom|CrLf", context.Expand("${languageId}|${encoding}|${lineEnding}"));
    }

    [Fact]
    public void Context_expands_workspace_folder_from_nearest_git_marker()
    {
        var root = Path.Combine(Path.GetTempPath(), $"azunote-workspace-{Guid.NewGuid():N}");
        var nested = Path.Combine(root, "src", "nested");
        var path = Path.Combine(nested, "notes.md");
        try
        {
            Directory.CreateDirectory(nested);
            Directory.CreateDirectory(Path.Combine(root, ".git"));

            var context = new ExternalToolContext(path, string.Empty, string.Empty);

            Assert.Equal(Path.GetFullPath(root), context.WorkspaceFolder);
            Assert.Equal(Path.GetFullPath(root), context.Expand("${workspaceFolder}"));
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
    public void Context_expands_workspace_folder_from_root_editorconfig()
    {
        var root = Path.Combine(Path.GetTempPath(), $"azunote-workspace-{Guid.NewGuid():N}");
        var nested = Path.Combine(root, "src", "nested");
        var path = Path.Combine(nested, "notes.md");
        try
        {
            Directory.CreateDirectory(nested);
            File.WriteAllText(
                Path.Combine(root, ".editorconfig"),
                "root = true\n[*]\nindent_size = 2\n");

            var context = new ExternalToolContext(path, string.Empty, string.Empty);

            Assert.Equal(Path.GetFullPath(root), context.WorkspaceFolder);
            Assert.Equal(Path.GetFullPath(root), context.Expand("${workspaceFolder}"));
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
    public void Context_leaves_workspace_folder_empty_without_a_root_marker()
    {
        var root = Path.Combine(Path.GetTempPath(), $"azunote-workspace-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "src", "notes.md");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var context = new ExternalToolContext(path, string.Empty, string.Empty);

            Assert.Null(context.WorkspaceFolder);
            Assert.Equal("", context.Expand("${workspaceFolder}"));
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
    public void Availability_evaluates_all_when_conditions_and_visibility()
    {
        var settings = new ExternalToolSettings
        {
            Name = "Format",
            Launch = new ExternalToolLaunchSettings { Command = "cmd.exe" },
            Visibility = "always",
            When = new ExternalToolWhenSettings
            {
                Extensions = [".md"],
                Languages = ["markdown"],
                File = "backed",
                Selection = "nonEmpty",
                Document = "dirty",
                Os = ["windows"]
            }
        };
        var context = new ExternalToolContext(
            Path.Combine("folder", "notes.md"),
            Path.Combine("folder", "notes.md"),
            "document",
            "selection",
            languageId: "markdown",
            isDirty: true);

        var state = ExternalToolAvailability.Evaluate(settings, context);

        Assert.True(state.IsVisible);
        Assert.True(state.IsEnabled);
        Assert.Null(state.DisabledReason);
    }

    [Fact]
    public void Availability_hides_when_available_tools_when_conditions_do_not_match()
    {
        var settings = new ExternalToolSettings
        {
            Name = "Format",
            Launch = new ExternalToolLaunchSettings { Command = "cmd.exe" },
            Visibility = "whenAvailable",
            When = new ExternalToolWhenSettings
            {
                Extensions = [".md"]
            }
        };
        var context = new ExternalToolContext(
            Path.Combine("folder", "notes.txt"),
            Path.Combine("folder", "notes.txt"),
            "document",
            string.Empty);

        var state = ExternalToolAvailability.Evaluate(settings, context);

        Assert.False(state.IsVisible);
        Assert.False(state.IsEnabled);
        Assert.NotNull(state.DisabledReason);
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
        var result = await ExternalToolRunner.RunAsync(
            definition,
            new ExternalToolContext(null, "stdin payload", string.Empty));

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal("stdin payload", result.StandardOutput.TrimEnd('\r', '\n'));
    }

    [Fact]
    public async Task Runner_launches_cmd_scripts_automatically()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"azunote external tool {Guid.NewGuid():N}");
        var scriptPath = Path.Combine(root, "echo arguments.cmd");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                scriptPath,
                "@echo off\r\necho %~1\r\n");

            var result = await ExternalToolRunner.RunAsync(
                new ExternalToolDefinition(scriptPath, ["value with spaces"]),
                new ExternalToolContext(null, string.Empty, string.Empty));

            Assert.True(result.Succeeded, result.StandardError);
            Assert.Equal("value with spaces", result.StandardOutput.Trim());
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
    public async Task Runner_launches_powershell_scripts_automatically()
    {
        if (!OperatingSystem.IsWindows()
            || !new[] { "pwsh.exe", "powershell.exe" }
                .Any(command => ExternalToolLaunchResolver.Resolve(command, null) is not null))
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"azunote external tool {Guid.NewGuid():N}");
        var scriptPath = Path.Combine(root, "echo arguments.ps1");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                scriptPath,
                "Write-Output $args[0]\r\n");

            var result = await ExternalToolRunner.RunAsync(
                new ExternalToolDefinition(scriptPath, ["value with spaces"]),
                new ExternalToolContext(null, string.Empty, string.Empty));

            Assert.True(result.Succeeded, result.StandardError);
            Assert.Equal("value with spaces", result.StandardOutput.Trim());
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

                [launch]
                command = "prettier"
                args = ["--write", "${file}"]
                input = "filePath"
                output = "reloadFile"
                """);

            var settings = await SettingsFileService.LoadAsync(root);
            var tool = Assert.Single(settings.ExternalTools);
            var definition = tool.ToDefinition();

            Assert.Equal("Format document", tool.Name);
            Assert.Equal("prettier", definition.FileName);
            Assert.Equal(["--write", "${file}"], definition.Arguments);
            Assert.Equal(ExternalToolInputMode.FilePath, definition.InputMode);
            Assert.Equal(ExternalToolOutputMode.ReloadFile, definition.OutputMode);
            Assert.Equal(Path.GetFullPath(toolPath), tool.DefinitionPath);

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
    public async Task Settings_service_loads_terminal_configuration()
    {
        var root = Path.Combine(Path.GetTempPath(), $"azunyan-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(
                SettingsFileService.GetSettingsFilePath(root),
                """
                [terminal]
                command = "conhost.exe"
                args = ["cmd.exe", "/K", "cd", "/d", "${documentDir}"]
                workingDirectory = "${documentDir}"

                [explorer]
                command = "explorer.exe"
                args = ["/select,\"${file}\""]
                workingDirectory = "${documentDir}"
                """);

            var settings = await SettingsFileService.LoadAsync(root);

            Assert.Equal("conhost.exe", settings.Terminal.Command);
            Assert.Equal(
                ["cmd.exe", "/K", "cd", "/d", "${documentDir}"],
                settings.Terminal.Arguments);
            Assert.Equal("${documentDir}", settings.Terminal.WorkingDirectory);
            Assert.Equal("explorer.exe", settings.Explorer.Command);
            Assert.Equal(["/select,\"${file}\""], settings.Explorer.Arguments);
            Assert.Equal("${documentDir}", settings.Explorer.WorkingDirectory);
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
    public async Task Settings_service_loads_nested_tool_definition_with_when_and_environment()
    {
        var root = Path.Combine(Path.GetTempPath(), $"azunote-settings-{Guid.NewGuid():N}");
        var tools = Path.Combine(root, SettingsFileService.ToolsDirectoryName);
        var toolPath = Path.Combine(tools, "Format.tool.toml");
        Directory.CreateDirectory(tools);
        try
        {
            await File.WriteAllTextAsync(
                toolPath,
                """
                name = "Format document"
                shortcut = "Alt+Shift+F"
                visibility = "whenAvailable"

                [launch]
                command = "cmd.exe"
                args = ["/c", "more"]
                workingDirectory = "${documentDir}"
                input = "document"
                output = "replaceDocument"

                [when]
                extensions = [".md"]
                languages = ["markdown"]
                file = "backed"
                selection = "any"
                document = "dirty"
                os = ["windows"]

                [env]
                NODE_ENV = "development"
                """);

            var settings = await SettingsFileService.LoadAsync(root);
            var tool = Assert.Single(settings.ExternalTools);

            Assert.Equal("Alt+Shift+F", tool.Shortcut);
            Assert.Equal("whenAvailable", tool.Visibility);
            Assert.Equal("cmd.exe", tool.ToDefinition().FileName);
            Assert.Equal(["/c", "more"], tool.ToDefinition().Arguments);
            Assert.Equal("development", tool.ToDefinition().Environment["NODE_ENV"]);
            Assert.Equal([".md"], tool.When.Extensions);
            Assert.Equal("backed", tool.When.File);
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

                [launch]
                command = "markdown-preview"
                input = "document"
                output = "newDocument"
                """);

            var settings = await SettingsFileService.LoadAsync(root);

            var packages = Assert.Single(settings.ExternalToolMenu);
            var bundleNode = Assert.Single(packages.Children);
            Assert.Equal("Markdown preview", bundleNode.Name);
            Assert.True(bundleNode.IsTool);
            Assert.Equal("Markdown preview", Assert.Single(settings.ExternalTools).Name);
            Assert.EndsWith("markdown.tool", bundleNode.Tool!.DefinitionDirectory, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(
                Path.GetFullPath(Path.Combine(bundleNode.Tool.DefinitionDirectory!, "manifest.toml")),
                bundleNode.Tool.DefinitionPath);
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
            Assert.Contains("[debug]", settingsText, StringComparison.Ordinal);
            Assert.Contains("[terminal]", settingsText, StringComparison.Ordinal);
            Assert.Contains("[explorer]", settingsText, StringComparison.Ordinal);
            var settings = await SettingsFileService.LoadAsync(root);
            Assert.Equal("wt.exe", settings.Terminal.Command);
            Assert.Equal("explorer.exe", settings.Explorer.Command);
            Assert.Contains(settings.ExternalTools, tool => tool.Name == "Prettier");
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
