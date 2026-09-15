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
}
