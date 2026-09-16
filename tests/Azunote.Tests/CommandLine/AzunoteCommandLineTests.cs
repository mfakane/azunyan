using Xunit;

namespace Azunote.Tests.CommandLine;

public sealed class AzunoteCommandLineTests
{
    [Theory]
    [InlineData("none", CommandLineOutputTarget.None)]
    [InlineData("filePath", CommandLineOutputTarget.FilePath)]
    [InlineData("document", CommandLineOutputTarget.Document)]
    [InlineData("selection", CommandLineOutputTarget.Selection)]
    public void Output_accepts_the_external_tool_input_vocabulary(
        string value,
        CommandLineOutputTarget expected)
    {
        Assert.Equal(expected, AzunoteCommandLine.Parse(["--output", value]).Output);
        Assert.Equal(expected, AzunoteCommandLine.Parse([$"--output={value}"]).Output);
        Assert.Equal(expected, AzunoteCommandLine.Parse(["-o", value]).Output);
    }

    [Fact]
    public void Requesting_output_waits_for_the_document_window()
    {
        var options = AzunoteCommandLine.Parse(["--output", "document", "-"]);

        Assert.True(options.WaitForExit);
        Assert.True(options.ReadStandardInput);
        Assert.Equal(CommandLineOutputTarget.Document, options.Output);
    }

    [Fact]
    public void Requesting_no_output_leaves_waiting_to_the_caller()
    {
        var options = AzunoteCommandLine.Parse(["--output", "none", "notes.txt"]);

        Assert.False(options.WaitForExit);
        Assert.Equal(CommandLineOutputTarget.None, options.Output);
        Assert.Equal("notes.txt", options.FilePath);
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

        Assert.Equal("--output", options.FilePath);
        Assert.Equal(CommandLineOutputTarget.None, options.Output);
    }

    [Fact]
    public void A_document_position_can_be_written_as_a_short_form()
    {
        var options = AzunoteCommandLine.Parse(["+12:3", "notes.txt"]);

        Assert.Equal(12, options.Line);
        Assert.Equal(3, options.Column);
        Assert.Equal("notes.txt", options.FilePath);
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
    public void A_bare_dash_reads_standard_input()
    {
        var options = AzunoteCommandLine.Parse(["-"]);

        Assert.True(options.ReadStandardInput);
        Assert.Null(options.FilePath);
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
