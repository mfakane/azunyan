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
        Assert.EndsWith("folder", context.FileDirname, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            $"{context.FilePath}|{context.FileDirname}|notes.md|whole document|selected text|12|7|{ExternalToolContext.UserHome}|{context.Cwd}|{context.ExecPath}|{context.PathSeparator}|{context.PathSeparator}",
            context.Expand("${file}|${fileDirname}|${fileBasename}|${document}|${selectedText}|${lineNumber}|${columnNumber}|${userHome}|${cwd}|${execPath}|${pathSeparator}|${/}"));
        Assert.Equal(
            Environment.GetEnvironmentVariable("PATH") ?? string.Empty,
            context.Expand("${env:PATH}"));
        Assert.Equal("selected text", context.GetInput(ExternalToolInputMode.Selection));
        Assert.Equal(
            "${fileName}|${selection}|${filePath}",
            context.Expand("${fileName}|${selection}|${filePath}"));
        Assert.Equal("", context.Expand("${input}"));
        Assert.Equal("per item", context.WithInput("per item").Expand("${input}"));
    }

    [Fact]
    public void External_tool_definition_defaults_to_one_empty_stdin_run()
    {
        var definition = new ExternalToolDefinition("formatter");

        Assert.Equal(ExternalToolInputMode.None, definition.InputMode);
        Assert.Equal(ExternalToolPerMode.None, definition.Per.Mode);
        Assert.Equal(string.Empty, definition.Stdin);
        Assert.Equal(ExternalToolOutputActions.Ignore, definition.Output);
        Assert.Equal(ExternalToolOutputActions.Ignore, definition.Stdout);
        Assert.Equal(ExternalToolOutputActions.Ignore, definition.Stderr);
    }

    [Fact]
    public void External_tool_per_line_splits_all_line_endings_and_preserves_empty_parts()
    {
        var per = ExternalToolPer.Parse("line");

        Assert.Equal(
            ["first", string.Empty, "third", string.Empty],
            per.Split("first\r\n\nthird\r"));
    }

    [Fact]
    public void External_tool_per_regex_preserves_empty_parts_and_capture_groups()
    {
        var per = ExternalToolPer.Parse("regex:(,)");

        Assert.Equal(["a", ",", string.Empty, ",", "b"], per.Split("a,,b"));
    }

    [Fact]
    public void External_tool_per_rejects_invalid_values()
    {
        Assert.Throws<ArgumentException>(() => ExternalToolPer.Parse("regex:"));
        Assert.Throws<ArgumentException>(() => ExternalToolPer.Parse("regex:["));
        Assert.Throws<ArgumentException>(() => ExternalToolPer.Parse("paragraph"));
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
        Assert.Equal(Path.GetFileName(temporaryPath), context.FileBasename);
        Assert.Equal(Path.GetFileNameWithoutExtension(temporaryPath), context.FileBasenameNoExtension);
        Assert.Equal(".md", context.FileExtname);
        Assert.Equal(context.FileDirname, context.FileDirname);
        Assert.Equal(
            Path.GetFileName(Path.TrimEndingDirectorySeparator(context.FileDirname!)),
            context.FileDirnameBasename);
        Assert.Equal(
            $"{context.FileBasenameNoExtension}|{context.FileExtname}|{context.FileDirnameBasename}",
            context.Expand("${fileBasenameNoExtension}|${fileExtname}|${fileDirnameBasename}"));
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
            Assert.Equal(
                Path.GetFileName(root),
                context.Expand("${workspaceFolderBasename}"));
            Assert.Equal(
                Path.GetFullPath(root),
                context.Expand("${fileWorkspaceFolder}"));
            Assert.Equal(
                Path.GetRelativePath(root, path),
                context.Expand("${relativeFile}"));
            Assert.Equal(
                Path.GetRelativePath(root, nested),
                context.Expand("${relativeFileDirname}"));
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
            Assert.Equal("||", context.Expand("${relativeFile}|${relativeFileDirname}|${fileWorkspaceFolder}"));
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
    public async Task DotEnv_loader_uses_the_nearest_ancestor_and_parses_supported_values()
    {
        var root = Path.Combine(Path.GetTempPath(), $"azunote-dotenv-{Guid.NewGuid():N}");
        var nested = Path.Combine(root, "src", "nested");
        try
        {
            Directory.CreateDirectory(nested);
            await File.WriteAllTextAsync(
                Path.Combine(root, ".env"),
                "ROOT_VALUE=root\nSHOULD_NOT_BE_USED=root\n");
            await File.WriteAllTextAsync(
                Path.Combine(nested, ".env"),
                "# comment\n"
                + "ROOT_VALUE=nested\n"
                + "export PLAIN_VALUE = plain\n"
                + "SINGLE_VALUE = 'single value'\n"
                + "DOUBLE_VALUE = \"double value\" # comment\n"
                + "EMPTY_VALUE=\n"
                + "DUPLICATE_VALUE=first\n"
                + "DUPLICATE_VALUE=last\n"
                + "INLINE_VALUE=value # comment\n"
                + "HASH_VALUE=value#not-comment\n"
                + "INVALID-NAME=ignored\n"
                + "INVALID_LINE\n"
                + "UNMATCHED_VALUE=\"ignored\n");

            var values = DotEnvFileLoader.Load(nested);

            Assert.Equal("nested", values["ROOT_VALUE"]);
            Assert.False(values.ContainsKey("SHOULD_NOT_BE_USED"));
            Assert.Equal("plain", values["PLAIN_VALUE"]);
            Assert.Equal("single value", values["SINGLE_VALUE"]);
            Assert.Equal("double value", values["DOUBLE_VALUE"]);
            Assert.Equal(string.Empty, values["EMPTY_VALUE"]);
            Assert.Equal("last", values["DUPLICATE_VALUE"]);
            Assert.Equal("value", values["INLINE_VALUE"]);
            Assert.Equal("value#not-comment", values["HASH_VALUE"]);
            Assert.False(values.ContainsKey("INVALID-NAME"));
            Assert.False(values.ContainsKey("INVALID_LINE"));
            Assert.False(values.ContainsKey("UNMATCHED_VALUE"));
            Assert.Empty(DotEnvFileLoader.Load(null));
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
    public void External_tool_environment_resolves_dotenv_and_tool_values_for_expansion()
    {
        var root = Path.Combine(Path.GetTempPath(), $"azunote-dotenv-{Guid.NewGuid():N}");
        var documentDirectory = Path.Combine(root, "src");
        var documentPath = Path.Combine(documentDirectory, "notes.md");
        var dotenvName = $"AZUNOTE_DOTENV_{Guid.NewGuid():N}";
        var overrideName = $"AZUNOTE_OVERRIDE_{Guid.NewGuid():N}";
        try
        {
            Directory.CreateDirectory(documentDirectory);
            File.WriteAllText(
                Path.Combine(root, ".env"),
                $"{dotenvName}=from dotenv\n{overrideName}=from dotenv\n");

            var context = new ExternalToolContext(
                documentPath,
                documentPath,
                string.Empty,
                string.Empty);
            var definition = new ExternalToolDefinition(
                "test-command",
                environment: new Dictionary<string, string>
                {
                    [overrideName] = "from tool",
                    ["AZUNOTE_DERIVED"] = $"prefix-${{env:{dotenvName}}}"
                });

            var environment = ExternalToolEnvironmentResolver.Resolve(definition, context);

            Assert.Equal("from dotenv", environment.Values[dotenvName]);
            Assert.Equal("from tool", environment.Values[overrideName]);
            Assert.Equal("prefix-from dotenv", environment.Values["AZUNOTE_DERIVED"]);
            Assert.Equal("from dotenv", environment.Overrides[dotenvName]);
            Assert.Equal("from tool", environment.Overrides[overrideName]);
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
    public void Availability_resolves_dotenv_command_values()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), $"azunote-dotenv-{Guid.NewGuid():N}");
        var documentDirectory = Path.Combine(root, "src");
        var documentPath = Path.Combine(documentDirectory, "notes.md");
        var scriptPath = Path.Combine(root, "tool.cmd");
        var commandName = $"AZUNOTE_COMMAND_{Guid.NewGuid():N}";
        try
        {
            Directory.CreateDirectory(documentDirectory);
            File.WriteAllText(Path.Combine(root, ".env"), $"{commandName}={scriptPath}\n");
            File.WriteAllText(scriptPath, "@echo off\r\n");

            var settings = new ExternalToolSettings
            {
                Name = "Dotenv command",
                Launch = new ExternalToolLaunchSettings
                {
                    Command = $"${{env:{commandName}}}"
                }
            };
            var state = ExternalToolAvailability.Evaluate(
                settings,
                new ExternalToolContext(documentPath, documentPath, string.Empty, string.Empty));

            Assert.True(state.IsEnabled, state.DisabledReason);
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
    public void Output_interpreter_selects_actions_for_the_mixed_and_individual_streams()
    {
        var replace = ExternalToolOutputInterpreter.Interpret(
            new ExternalToolDefinition(
                "formatter",
                output: new ExternalToolOutputActions(
                    ExternalToolOutputMode.ReplaceDocument,
                    ExternalToolOutputMode.NewDocument),
                stdout: new ExternalToolOutputActions(
                    ExternalToolOutputMode.ReplaceSelection,
                    ExternalToolOutputMode.Ignore)),
            new ExternalToolResult(0, "formatted", "diagnostic", "mixed"));
        var failure = ExternalToolOutputInterpreter.Interpret(
            new ExternalToolDefinition(
                "formatter",
                stderr: new ExternalToolOutputActions(
                    ExternalToolOutputMode.Ignore,
                    ExternalToolOutputMode.NewDocument)),
            new ExternalToolResult(2, "partial", "bad input", "partialbad input"));

        Assert.Equal(
            [
                new ExternalToolOutputAction(
                    ExternalToolOutputChannel.Mixed,
                    ExternalToolOutputMode.ReplaceDocument,
                    "mixed"),
                new ExternalToolOutputAction(
                    ExternalToolOutputChannel.Stdout,
                    ExternalToolOutputMode.ReplaceSelection,
                    "formatted")
            ],
            replace.Actions);
        var failureAction = Assert.Single(failure.Actions);
        Assert.Equal(ExternalToolOutputChannel.Stderr, failureAction.Stream);
        Assert.Equal(ExternalToolOutputMode.NewDocument, failureAction.Mode);
        Assert.Equal("bad input", failureAction.Text);
    }

    [Fact]
    public void A_single_output_action_applies_to_both_exit_statuses()
    {
        var definition = new ExternalToolDefinition(
            "prettier",
            output: new ExternalToolOutputActions(
                ExternalToolOutputMode.ReloadFile,
                ExternalToolOutputMode.ReloadFile));
        var success = ExternalToolOutputInterpreter.Interpret(
            definition,
            new ExternalToolResult(0, "ignored", string.Empty, "mixed"));
        var failure = ExternalToolOutputInterpreter.Interpret(
            definition,
            new ExternalToolResult(1, "ignored", string.Empty, "mixed"));

        Assert.Equal(ExternalToolOutputMode.ReloadFile, Assert.Single(success.Actions).Mode);
        Assert.Equal(ExternalToolOutputMode.ReloadFile, Assert.Single(failure.Actions).Mode);
        Assert.Equal("mixed", Assert.Single(success.Actions).Text);
    }

    [Fact]
    public void Output_interpreter_can_request_reload_without_using_stream_text()
    {
        var output = ExternalToolOutputInterpreter.Interpret(
            new ExternalToolDefinition(
                "prettier",
                output: new ExternalToolOutputActions(
                    ExternalToolOutputMode.ReloadFile,
                    ExternalToolOutputMode.Ignore)),
            new ExternalToolResult(0, "ignored", string.Empty, "mixed"));

        var action = Assert.Single(output.Actions);
        Assert.Equal(ExternalToolOutputMode.ReloadFile, action.Mode);
        Assert.Equal("mixed", action.Text);
    }

    [Fact]
    public async Task Runner_passes_document_on_stdin_and_captures_stdout()
    {
        var isWindows = OperatingSystem.IsWindows();
        var definition = new ExternalToolDefinition(
            isWindows ? "cmd.exe" : "/bin/cat",
            ExternalToolDefinition.ParseArguments(isWindows ? "/c more" : string.Empty),
            inputMode: ExternalToolInputMode.Document,
            stdin: "${input}");
        var result = await ExternalToolRunner.RunAsync(
            definition,
            new ExternalToolContext(null, "stdin payload", string.Empty));

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal("stdin payload", result.StandardOutput.TrimEnd('\r', '\n'));
        Assert.Equal(result.StandardOutput, result.MixedOutput);
    }

    [Fact]
    public async Task Runner_does_not_send_input_without_stdin_configuration()
    {
        var isWindows = OperatingSystem.IsWindows();
        var definition = new ExternalToolDefinition(
            isWindows ? "cmd.exe" : "/bin/cat",
            ExternalToolDefinition.ParseArguments(isWindows ? "/c more" : string.Empty),
            inputMode: ExternalToolInputMode.Document);
        var result = await ExternalToolRunner.RunAsync(
            definition,
            new ExternalToolContext(null, "must not be sent", string.Empty));

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal(string.Empty, result.StandardOutput.Trim());
        Assert.Empty(result.StandardError);
        Assert.Equal(string.Empty, result.MixedOutput.Trim());
    }

    [Fact]
    public async Task Runner_captures_stdout_stderr_and_their_mixed_stream()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"azunote external mixed {Guid.NewGuid():N}");
        var scriptPath = Path.Combine(root, "write mixed.cmd");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                scriptPath,
                "@echo off\r\necho standard output\r\necho standard error 1>&2\r\n");

            var result = await ExternalToolRunner.RunAsync(
                new ExternalToolDefinition(scriptPath),
                new ExternalToolContext(null, string.Empty, string.Empty));

            Assert.True(result.Succeeded, result.StandardError);
            Assert.Contains("standard output", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("standard error", result.StandardError, StringComparison.Ordinal);
            Assert.Contains("standard output", result.MixedOutput, StringComparison.Ordinal);
            Assert.Contains("standard error", result.MixedOutput, StringComparison.Ordinal);
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
    public async Task Runner_splits_input_per_line_and_continues_after_a_failure()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"azunote external per {Guid.NewGuid():N}");
        var scriptPath = Path.Combine(root, "echo per.cmd");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                scriptPath,
                "@echo off\r\n"
                + "set /p VALUE=\r\n"
                + "echo [%VALUE%]\r\n"
                + "if \"%VALUE%\"==\"bad\" exit /b 7\r\n");

            var result = await ExternalToolRunner.RunAsync(
                new ExternalToolDefinition(
                    scriptPath,
                    inputMode: ExternalToolInputMode.Document,
                    per: "line",
                    stdin: "${input}"),
                new ExternalToolContext(null, "first\nbad\nlast", string.Empty));

            Assert.False(result.Succeeded);
            Assert.Equal(7, result.ExitCode);
            Assert.Equal(
                "[first]\r\n[bad]\r\n[last]\r\n",
                result.StandardOutput);
            Assert.Equal(result.StandardOutput, result.MixedOutput);
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
    public async Task Runner_expands_dotenv_values_and_passes_them_to_the_child_process()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"azunote external dotenv {Guid.NewGuid():N}");
        var documentDirectory = Path.Combine(root, "src");
        var documentPath = Path.Combine(documentDirectory, "notes.md");
        var dotenvName = $"AZUNOTE_DOTENV_{Guid.NewGuid():N}";
        var overrideName = $"AZUNOTE_OVERRIDE_{Guid.NewGuid():N}";
        var scriptPath = Path.Combine(root, "echo environment.cmd");
        try
        {
            Directory.CreateDirectory(documentDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(root, ".env"),
                $"{dotenvName}=from dotenv\n{overrideName}=from dotenv\n");
            await File.WriteAllTextAsync(
                scriptPath,
                $"@echo off\r\necho %~1\r\necho %{dotenvName}%\r\necho %~2\r\necho %{overrideName}%\r\n");

            var definition = new ExternalToolDefinition(
                scriptPath,
                [
                    $"${{env:{dotenvName}}}",
                    $"${{env:{overrideName}}}"
                ],
                environment: new Dictionary<string, string>
                {
                    [overrideName] = "from tool"
                });
            var result = await ExternalToolRunner.RunAsync(
                definition,
                new ExternalToolContext(
                    documentPath,
                    documentPath,
                    string.Empty,
                    string.Empty));

            Assert.True(result.Succeeded, result.StandardError);
            Assert.Equal(
                ["from dotenv", "from dotenv", "from tool", "from tool"],
                result.StandardOutput
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
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
                per = "line"
                stdin = "${input}"
                output = ["reloadFile", "ignore"]
                stdout = "newDocument"
                stderr = ["ignore", "newDocument"]
                """);

            var settings = await SettingsFileService.LoadAsync(root);
            var tool = Assert.Single(settings.ExternalTools);
            var definition = tool.ToDefinition();

            Assert.Equal("Format document", tool.Name);
            Assert.Equal("prettier", definition.FileName);
            Assert.Equal(["--write", "${file}"], definition.Arguments);
            Assert.Equal(ExternalToolInputMode.FilePath, definition.InputMode);
            Assert.Equal(ExternalToolPerMode.Line, definition.Per.Mode);
            Assert.Equal("${input}", definition.Stdin);
            Assert.Equal(ExternalToolOutputMode.ReloadFile, definition.Output.OnSuccess);
            Assert.Equal(ExternalToolOutputMode.Ignore, definition.Output.OnFailure);
            Assert.Equal(ExternalToolOutputMode.NewDocument, definition.Stdout.OnSuccess);
            Assert.Equal(ExternalToolOutputMode.NewDocument, definition.Stdout.OnFailure);
            Assert.Equal(ExternalToolOutputMode.Ignore, definition.Stderr.OnSuccess);
            Assert.Equal(ExternalToolOutputMode.NewDocument, definition.Stderr.OnFailure);
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
                args = ["cmd.exe", "/K", "cd", "/d", "${documentDirname}"]
                workingDirectory = "${documentDirname}"

                [explorer]
                command = "explorer.exe"
                args = ["/select,\"${file}\""]
                workingDirectory = "${documentDirname}"
                """);

            var settings = await SettingsFileService.LoadAsync(root);

            Assert.Equal("conhost.exe", settings.Terminal.Command);
            Assert.Equal(
                ["cmd.exe", "/K", "cd", "/d", "${documentDirname}"],
                settings.Terminal.Arguments);
            Assert.Equal("${documentDirname}", settings.Terminal.WorkingDirectory);
            Assert.Equal("explorer.exe", settings.Explorer.Command);
            Assert.Equal(["/select,\"${file}\""], settings.Explorer.Arguments);
            Assert.Equal("${documentDirname}", settings.Explorer.WorkingDirectory);
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
                workingDirectory = "${documentDirname}"
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
