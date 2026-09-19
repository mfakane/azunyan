using Xunit;

namespace Azunote.Tests;

public sealed class ExternalToolStreamingTests
{
    [Fact]
    public void Stream_buffer_applies_whole_lines_and_holds_the_last_line_ending()
    {
        var buffer = new ExternalToolStreamBuffer();

        Assert.Equal(string.Empty, buffer.Append("first"));
        Assert.Equal("first", buffer.Append("\nsecond"));
        Assert.Equal("\nsecond", buffer.Append("\nthird"));
        Assert.Equal("\nthird", buffer.Complete());
    }

    [Fact]
    public void Stream_buffer_never_applies_a_line_ending_split_across_chunks()
    {
        var buffer = new ExternalToolStreamBuffer();

        // The carriage return is held back, because the line feed that
        // completes it can still arrive in the next chunk.
        Assert.Equal("line", buffer.Append("line\r"));
        Assert.Equal(string.Empty, buffer.FlushIdle());
        Assert.Equal(string.Empty, buffer.Append("\n"));
        Assert.Equal("\r\nnext", buffer.Append("next\r\n"));
        Assert.Equal("\r\n", buffer.Complete());
    }

    [Fact]
    public void Stream_buffer_holds_a_split_surrogate_pair_until_it_is_whole()
    {
        var buffer = new ExternalToolStreamBuffer();
        const string emoji = "\U0001F600";

        Assert.Equal(string.Empty, buffer.Append(emoji[..1]));
        Assert.Equal(string.Empty, buffer.FlushIdle());
        Assert.Equal(emoji, buffer.Append(emoji[1..]) + buffer.FlushIdle());
    }

    [Fact]
    public void Stream_buffer_flushes_a_line_that_is_still_being_written()
    {
        var buffer = new ExternalToolStreamBuffer();

        Assert.Equal(string.Empty, buffer.Append("50%"));
        Assert.Equal("50%", buffer.FlushIdle());
        Assert.Equal(string.Empty, buffer.FlushIdle());
        Assert.Equal(" done", buffer.Append(" done") + buffer.FlushIdle());
    }

    [Theory]
    [InlineData("")]
    [InlineData("one")]
    [InlineData("one\n")]
    [InlineData("one\ntwo")]
    [InlineData("one\r\ntwo\r\n")]
    [InlineData("\n\n\n")]
    [InlineData("trailing\n\n")]
    public void Stream_buffer_applies_exactly_the_text_it_was_given(string text)
    {
        foreach (var size in new[] { 1, 2, 3, 4096 })
        {
            var buffer = new ExternalToolStreamBuffer();
            var applied = string.Empty;
            for (var index = 0; index < text.Length; index += size)
            {
                applied += buffer.Append(
                    text.Substring(index, Math.Min(size, text.Length - index)));
            }

            applied += buffer.Complete();
            Assert.Equal(text, applied);
        }
    }

    [Fact]
    public void Streamed_channels_reject_an_action_chosen_by_exit_code()
    {
        var error = ExternalToolStreaming.Describe(
            ExternalToolStreamChannels.Stdout,
            ExternalToolOutputActions.Ignore,
            new ExternalToolOutputActions(
                ExternalToolOutputMode.NewDocument,
                ExternalToolOutputMode.Ignore),
            ExternalToolOutputActions.Ignore);

        Assert.NotNull(error);
        Assert.Contains("exit", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Streamed_channels_reject_actions_that_need_the_whole_output()
    {
        foreach (var mode in new[]
                 {
                     ExternalToolOutputMode.ReloadFile,
                     ExternalToolOutputMode.ShowCompletion
                 })
        {
            var error = ExternalToolStreaming.Describe(
                ExternalToolStreamChannels.Stdout,
                ExternalToolOutputActions.Ignore,
                new ExternalToolOutputActions(mode, mode),
                ExternalToolOutputActions.Ignore);

            Assert.NotNull(error);
            Assert.Contains("while the tool runs", error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Streamed_channels_reject_output_together_with_stdout_and_duplicate_actions()
    {
        var newDocument = new ExternalToolOutputActions(
            ExternalToolOutputMode.NewDocument,
            ExternalToolOutputMode.NewDocument);

        var both = ExternalToolStreaming.Describe(
            ExternalToolStreamChannels.Mixed | ExternalToolStreamChannels.Stdout,
            newDocument,
            newDocument,
            ExternalToolOutputActions.Ignore);
        Assert.NotNull(both);
        Assert.Contains("same text", both, StringComparison.Ordinal);

        var duplicate = ExternalToolStreaming.Describe(
            ExternalToolStreamChannels.Stdout | ExternalToolStreamChannels.Stderr,
            ExternalToolOutputActions.Ignore,
            newDocument,
            newDocument);
        Assert.NotNull(duplicate);
        Assert.Contains("same action", duplicate, StringComparison.Ordinal);
    }

    [Fact]
    public void Streamed_channels_accept_one_action_per_channel()
    {
        Assert.Null(ExternalToolStreaming.Describe(
            ExternalToolStreamChannels.Stdout | ExternalToolStreamChannels.Stderr,
            ExternalToolOutputActions.Ignore,
            new ExternalToolOutputActions(
                ExternalToolOutputMode.ReplaceSelection,
                ExternalToolOutputMode.ReplaceSelection),
            new ExternalToolOutputActions(
                ExternalToolOutputMode.NewDocument,
                ExternalToolOutputMode.NewDocument)));
    }

    [Fact]
    public async Task Settings_service_reads_a_stream_channel_name_and_array()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"azunyan-settings-stream-{Guid.NewGuid():N}");
        var tools = Path.Combine(root, SettingsFileService.ToolsDirectoryName);
        Directory.CreateDirectory(tools);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(tools, "one.tool.toml"),
                """
                name = "One channel"

                [launch]
                cmd = "echo Hello"
                stdout = "newDocument"
                stream = "stdout"
                """);
            await File.WriteAllTextAsync(
                Path.Combine(tools, "two.tool.toml"),
                """
                name = "Two channels"

                [launch]
                cmd = "echo Hello"
                stdout = "newDocument"
                stderr = "replaceSelection"
                stream = ["stdout", "stderr"]
                """);

            var settings = await SettingsFileService.LoadAsync(root);
            var definitions = settings.ExternalTools
                .ToDictionary(tool => tool.Name, tool => tool.ToDefinition());

            Assert.Equal(
                ExternalToolStreamChannels.Stdout,
                definitions["One channel"].Stream);
            Assert.Equal(
                ExternalToolStreamChannels.Stdout | ExternalToolStreamChannels.Stderr,
                definitions["Two channels"].Stream);
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
    public void Settings_reject_a_stream_channel_that_cannot_be_applied_while_running()
    {
        var settings = new ExternalToolSettings
        {
            Name = "Completion",
            Launch = new ExternalToolLaunchSettings
            {
                Cmd = "echo Hello",
                Stdout = new ExternalToolOutputActions(
                    ExternalToolOutputMode.ShowCompletion,
                    ExternalToolOutputMode.ShowCompletion),
                Stream = ExternalToolStreamChannels.Stdout
            }
        };

        var exception = Assert.Throws<SettingsFileException>(() => settings.ToDefinition());
        Assert.Contains("Completion", exception.Message, StringComparison.Ordinal);
        Assert.Contains("showCompletion", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_definition_rejects_a_stream_channel_it_cannot_apply()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new ExternalToolDefinition(
                "tool.exe",
                stdout: new ExternalToolOutputActions(
                    ExternalToolOutputMode.ReloadFile,
                    ExternalToolOutputMode.ReloadFile),
                stream: ExternalToolStreamChannels.Stdout));

        Assert.Contains("reloadFile", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Interpreting_a_result_leaves_a_streamed_channel_to_the_run()
    {
        var definition = new ExternalToolDefinition(
            "tool.exe",
            stdout: new ExternalToolOutputActions(
                ExternalToolOutputMode.NewDocument,
                ExternalToolOutputMode.NewDocument),
            stderr: new ExternalToolOutputActions(
                ExternalToolOutputMode.ReplaceSelection,
                ExternalToolOutputMode.ReplaceSelection),
            stream: ExternalToolStreamChannels.Stdout);

        var output = ExternalToolOutputInterpreter.Interpret(
            definition,
            new ExternalToolResult(0, "out", "err", "outerr"));

        var action = Assert.Single(output.Actions);
        Assert.Equal(ExternalToolOutputChannel.Stderr, action.Stream);
        Assert.Equal(ExternalToolOutputMode.ReplaceSelection, action.Mode);
    }

    [Fact]
    public async Task A_streamed_pwsh_tool_applies_its_values_as_it_produces_them()
    {
        if (!OperatingSystem.IsWindows()
            || ExternalToolLaunchResolver.ResolvePowerShell() is null)
        {
            return;
        }

        var newDocument = new ExternalToolOutputActions(
            ExternalToolOutputMode.NewDocument,
            ExternalToolOutputMode.NewDocument);
        var definition = new ExternalToolDefinition(
            "'A'; Start-Sleep -Milliseconds 400; 'B'",
            commandMode: ExternalToolCommandMode.Pwsh,
            stdout: newDocument,
            stream: ExternalToolStreamChannels.Stdout);
        var sink = new CollectingStreamSink();

        var result = await ExternalToolRunner.RunAsync(
            definition,
            new ExternalToolContext(null, string.Empty, string.Empty),
            warmPool: null,
            sink);

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal("A" + Environment.NewLine + "B", result.StandardOutput);
        Assert.Equal(result.StandardOutput, sink.Text);
        // A buffered run would have arrived as one write once the tool exited.
        Assert.True(sink.Writes > 1, $"Expected more than one write, got {sink.Writes}.");
    }

    [Fact]
    public async Task A_buffered_pwsh_tool_still_joins_its_values_without_a_trailing_newline()
    {
        if (!OperatingSystem.IsWindows()
            || ExternalToolLaunchResolver.ResolvePowerShell() is null)
        {
            return;
        }

        var result = await ExternalToolRunner.RunAsync(
            new ExternalToolDefinition(
                "'A'; 'B'",
                commandMode: ExternalToolCommandMode.Pwsh),
            new ExternalToolContext(null, string.Empty, string.Empty));

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal("A" + Environment.NewLine + "B", result.StandardOutput);
    }

    private sealed class CollectingStreamSink : IExternalToolStreamSink
    {
        private readonly System.Text.StringBuilder _text = new();

        public string Text
        {
            get
            {
                lock (_text)
                {
                    return _text.ToString();
                }
            }
        }

        public int Writes { get; private set; }

        public void Write(ExternalToolOutputChannel channel, string text)
        {
            Assert.Equal(ExternalToolOutputChannel.Stdout, channel);
            lock (_text)
            {
                Writes++;
                _text.Append(text);
            }
        }
    }
}
