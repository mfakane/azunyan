using Xunit;

namespace Azunote.Tests.CommandLine;

public sealed class AzunoteCommandLineTests
{
    [Theory]
    [InlineData("filePath", CommandLineOutputTarget.FilePath)]
    [InlineData("document", CommandLineOutputTarget.Document)]
    [InlineData("selection", CommandLineOutputTarget.Selection)]
    public void Output_accepts_the_external_tool_input_vocabulary(
        string value,
        CommandLineOutputTarget expected)
    {
        Assert.Equal([expected], AzunoteCommandLine.Parse(["--output", value]).Output);
        Assert.Equal([expected], AzunoteCommandLine.Parse([$"--output={value}"]).Output);
        Assert.Equal([expected], AzunoteCommandLine.Parse(["-o", value]).Output);
    }

    [Fact]
    public void Output_keeps_the_order_a_comma_separated_list_asked_for()
    {
        var options = AzunoteCommandLine.Parse(["--output", "selection,filePath", "--json"]);

        Assert.Equal(
            [CommandLineOutputTarget.Selection, CommandLineOutputTarget.FilePath],
            options.Output);
        Assert.True(options.Json);
        Assert.True(options.WaitForExit);
    }

    [Fact]
    public void More_than_one_output_value_needs_json()
    {
        var exception = Assert.Throws<CommandLineParseException>(
            () => AzunoteCommandLine.Parse(["--output", "filePath,document"]));

        Assert.Contains("--json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_needs_something_to_write()
    {
        Assert.Throws<CommandLineParseException>(
            () => AzunoteCommandLine.Parse(["--json", "notes.txt"]));
        Assert.Throws<CommandLineParseException>(
            () => AzunoteCommandLine.Parse(["--output", "none", "--json"]));
    }

    [Fact]
    public void Json_can_pin_the_format_of_a_single_output_value()
    {
        var options = AzunoteCommandLine.Parse(["--output", "document", "--json", "-"]);

        Assert.Equal([CommandLineOutputTarget.Document], options.Output);
        Assert.True(options.Json);
    }

    [Fact]
    public void No_output_cannot_be_asked_for_alongside_a_value()
    {
        var exception = Assert.Throws<CommandLineParseException>(
            () => AzunoteCommandLine.Parse(["--output", "none,document", "--json"]));

        Assert.Contains("none", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_output_value_is_not_accepted_twice()
    {
        var exception = Assert.Throws<CommandLineParseException>(
            () => AzunoteCommandLine.Parse(["--output", "document,document", "--json"]));

        Assert.Contains("document", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_output_value_is_rejected()
    {
        var exception = Assert.Throws<CommandLineParseException>(
            () => AzunoteCommandLine.Parse(["--output", "filePath,", "--json"]));

        Assert.Contains("filePath,", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_output_value_never_swallows_the_document_path()
    {
        var options = AzunoteCommandLine.Parse(["--output", "document", "notes.txt"]);

        Assert.Equal([CommandLineOutputTarget.Document], options.Output);
        Assert.Equal(["notes.txt"], options.FilePaths);
    }

    [Fact]
    public void Requesting_output_waits_for_the_document_window()
    {
        var options = AzunoteCommandLine.Parse(["--output", "document", "-"]);

        Assert.True(options.WaitForExit);
        Assert.True(options.ReadStandardInput);
        Assert.Equal([CommandLineOutputTarget.Document], options.Output);
        Assert.False(options.Json);
    }

    [Fact]
    public void Requesting_no_output_leaves_waiting_to_the_caller()
    {
        var options = AzunoteCommandLine.Parse(["--output", "none", "notes.txt"]);

        Assert.False(options.WaitForExit);
        Assert.Empty(options.Output);
        Assert.Equal(["notes.txt"], options.FilePaths);
    }

    [Fact]
    public void An_unknown_output_target_is_rejected()
    {
        var exception = Assert.Throws<CommandLineParseException>(
            () => AzunoteCommandLine.Parse(["--output", "clipboard"]));

        Assert.Contains("clipboard", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_output_target_is_rejected()
    {
        Assert.Throws<CommandLineParseException>(
            () => AzunoteCommandLine.Parse(["--output"]));
    }

    [Fact]
    public void Options_stop_at_the_argument_separator()
    {
        var options = AzunoteCommandLine.Parse(["--", "--output"]);

        Assert.Equal(["--output"], options.FilePaths);
        Assert.Empty(options.Output);
    }

    [Fact]
    public void A_document_position_can_be_written_as_a_short_form()
    {
        var options = AzunoteCommandLine.Parse(["+12:3", "notes.txt"]);

        Assert.Equal(12, options.Line);
        Assert.Equal(3, options.Column);
        Assert.Equal(["notes.txt"], options.FilePaths);
    }

    [Fact]
    public void A_line_short_form_leaves_the_column_alone()
    {
        var options = AzunoteCommandLine.Parse(["+12"]);

        Assert.Equal(12, options.Line);
        Assert.Null(options.Column);
    }

    [Fact]
    public void An_invalid_document_position_is_rejected()
    {
        var exception = Assert.Throws<CommandLineParseException>(
            () => AzunoteCommandLine.Parse(["+12:x"]));

        Assert.Contains("+12:x", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("first")]
    public void A_line_that_is_not_a_positive_integer_is_rejected(string value)
    {
        var exception = Assert.Throws<CommandLineParseException>(
            () => AzunoteCommandLine.Parse(["--line", value]));

        Assert.Contains(value, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_document_paths_open_in_the_order_they_were_named()
    {
        var options = AzunoteCommandLine.Parse(["notes.txt", "todo.md", "log.txt"]);

        Assert.Equal(["notes.txt", "todo.md", "log.txt"], options.FilePaths);
        Assert.False(options.WaitForExit);
    }

    [Theory]
    [InlineData("--line", "2")]
    [InlineData("--column", "2")]
    [InlineData("--output", "document")]
    public void Several_document_paths_take_no_option_about_one_document(
        string option,
        string value)
    {
        var exception = Assert.Throws<CommandLineParseException>(
            () => AzunoteCommandLine.Parse([option, value, "notes.txt", "todo.md"]));

        Assert.Contains(option, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_document_paths_are_not_waited_on()
    {
        var exception = Assert.Throws<CommandLineParseException>(
            () => AzunoteCommandLine.Parse(["--wait", "notes.txt", "todo.md"]));

        Assert.Contains("--wait", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void One_document_path_still_takes_every_option()
    {
        var options = AzunoteCommandLine.Parse(
            ["--wait", "+12:3", "--output", "document", "notes.txt"]);

        Assert.Equal(["notes.txt"], options.FilePaths);
        Assert.Equal(12, options.Line);
        Assert.True(options.WaitForExit);
    }

    [Fact]
    public void Everything_after_the_argument_separator_is_a_document_path()
    {
        var options = AzunoteCommandLine.Parse(["--", "--output", "-w"]);

        Assert.Equal(["--output", "-w"], options.FilePaths);
    }

    [Fact]
    public void A_document_path_that_is_only_whitespace_names_no_document()
    {
        var options = AzunoteCommandLine.Parse(["   "]);

        Assert.Empty(options.FilePaths);
    }

    [Fact]
    public void An_unknown_option_is_not_taken_for_one_of_several_paths()
    {
        var exception = Assert.Throws<CommandLineParseException>(
            () => AzunoteCommandLine.Parse(["notes.txt", "--verbose"]));

        Assert.Contains("--verbose", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bare_dash_reads_standard_input()
    {
        var options = AzunoteCommandLine.Parse(["-"]);

        Assert.True(options.ReadStandardInput);
        Assert.Empty(options.FilePaths);
    }

    [Fact]
    public void An_unknown_option_is_not_taken_for_a_document_path()
    {
        var exception = Assert.Throws<CommandLineParseException>(
            () => AzunoteCommandLine.Parse(["--verbose"]));

        Assert.Contains("--verbose", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_is_recognised_and_described()
    {
        Assert.True(AzunoteCommandLine.Parse(["--help"]).ShowHelp);
        Assert.True(AzunoteCommandLine.Parse(["-h"]).ShowHelp);
        Assert.Contains("--output", AzunoteCommandLine.Usage, StringComparison.Ordinal);
        Assert.Contains("+N[:M]", AzunoteCommandLine.Usage, StringComparison.Ordinal);
    }

    [Fact]
    public void An_option_name_is_completed_from_the_grammar()
    {
        var completions = Complete("azu --", out var handled);

        Assert.True(handled);
        Assert.Contains("--output", completions, StringComparer.Ordinal);
        Assert.Contains("--stdin", completions, StringComparer.Ordinal);
    }

    [Fact]
    public void An_output_target_is_completed_from_the_grammar()
    {
        var completions = Complete("azu --output ", out var handled);

        Assert.True(handled);
        Assert.Equal(
            ["document", "filePath", "none", "selection"],
            completions);
    }

    [Fact]
    public void An_output_target_is_completed_after_a_comma()
    {
        var completions = Complete("azu --output filePath,", out var handled);

        Assert.True(handled);
        Assert.Equal(
            ["filePath,document", "filePath,selection"],
            completions);
    }

    [Fact]
    public void An_ordinary_command_line_is_not_a_completion_request()
    {
        Assert.False(AzunoteCommandLine.TryCompleteArguments(
            ["notes.txt"],
            TextWriter.Null,
            TextWriter.Null,
            out _));
    }

    private static string[] Complete(string commandLine, out bool handled)
    {
        using var output = new StringWriter();
        handled = AzunoteCommandLine.TryCompleteArguments(
            [$"[suggest:{commandLine.Length}]", commandLine],
            output,
            TextWriter.Null,
            out var exitCode);
        Assert.Equal(0, exitCode);
        return output.ToString().Split(
            Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
