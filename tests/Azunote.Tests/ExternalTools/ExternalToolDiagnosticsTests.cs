using Azunote;
using Azunyan.Core;
using Xunit;

namespace Azunote.Tests.ExternalTools;

public sealed class ExternalToolDiagnosticsTests
{
    [Fact]
    public void Parse_reads_gnu_and_msbuild_lines_and_skips_the_rest()
    {
        var diagnostics = ExternalToolDiagnostics.Parse(
            "Checking 2 files\r\n"
            + "src/app.py:3:8: F401 `os` imported but unused\r\n"
            + @"C:\work\Program.cs(10,5,10,9): warning CS0168: The variable 'x' is declared but never used" + "\r\n"
            + "notes.md:7: note: consider a heading\n"
            + "Found 2 problems");

        Assert.Collection(
            diagnostics,
            first =>
            {
                Assert.Equal(new ExternalToolDiagnostic("src/app.py", 3, 8, null, null, DiagnosticSeverity.Error, "F401 `os` imported but unused"), first);
            },
            second =>
            {
                Assert.Equal(@"C:\work\Program.cs", second.File);
                Assert.Equal((10, 5, 10, 9), (second.Line, second.Column!.Value, second.EndLine!.Value, second.EndColumn!.Value));
                Assert.Equal(DiagnosticSeverity.Warning, second.Severity);
                Assert.Equal("The variable 'x' is declared but never used", second.Message);
            },
            third =>
            {
                Assert.Equal(("notes.md", 7, (int?)null), (third.File, third.Line, third.Column));
                Assert.Equal(DiagnosticSeverity.Information, third.Severity);
            });
    }

    [Fact]
    public void Parse_uses_a_pattern_of_the_tools_own()
    {
        var diagnostics = ExternalToolDiagnostics.Parse(
            "line 4, col 2, Warning - Missing semicolon",
            @"^line (?<line>\d+), col (?<column>\d+), (?<severity>\w+) - (?<message>.+)$");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(new ExternalToolDiagnostic(null, 4, 2, null, null, DiagnosticSeverity.Warning, "Missing semicolon"), diagnostic);
    }

    [Fact]
    public void A_pattern_without_line_or_message_is_rejected()
    {
        var exception = Assert.Throws<ArgumentException>(() => ExternalToolDiagnostics.CreatePattern(@"^(?<line>\d+)$"));
        Assert.Contains("(?<message>...)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tool_definition_carries_its_pattern_and_rejects_an_unusable_one()
    {
        var settings = new ExternalToolSettings
        {
            Name = "lint",
            Launch = new ExternalToolLaunchSettings
            {
                Command = "lint",
                Stdout = new ExternalToolOutputActions(ExternalToolOutputMode.Diagnostics, ExternalToolOutputMode.Diagnostics)
            },
            Diagnostics = new ExternalToolDiagnosticsSettings { Pattern = @"^(?<line>\d+) (?<message>.+)$" }
        };

        Assert.Equal(@"^(?<line>\d+) (?<message>.+)$", settings.ToDefinition().DiagnosticsPattern);

        settings.Diagnostics.Pattern = @"^(?<line>\d+)$";
        Assert.Throws<SettingsFileException>(() => settings.Validate());
        settings.Diagnostics.Pattern = "[";
        Assert.Throws<SettingsFileException>(() => settings.Validate());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("-", true)]
    [InlineData("<stdin>", true)]
    [InlineData(@"C:\work\src\app.py", true)]
    [InlineData("src/app.py", true)]
    [InlineData("./src/app.py", true)]
    [InlineData("app.py", true)]
    [InlineData("other.py", false)]
    [InlineData(@"C:\elsewhere\app.py", false)]
    public void A_diagnostic_belongs_to_the_document_it_names(string? file, bool expected)
    {
        Assert.Equal(expected, ExternalToolDiagnostics.IsForDocument(file, @"C:\work\src\app.py", null));
    }

    [Fact]
    public void A_diagnostic_for_the_temporary_copy_belongs_to_the_document()
    {
        Assert.True(ExternalToolDiagnostics.IsForDocument(@"C:\Temp\azunote-1.py", @"C:\work\app.py", @"C:\Temp\azunote-1.py"));
    }

    [Theory]
    // A column marks the word there.
    [InlineData(2, 5, null, null, 9, 13)]
    // An end marks up to it.
    [InlineData(2, 1, 2, 4, 5, 8)]
    // No column marks the line without its indentation.
    [InlineData(2, null, null, null, 9, 21)]
    // Off a word marks one character; at the end of a line, the last one.
    [InlineData(2, 11, null, null, 15, 16)]
    [InlineData(2, 17, null, null, 20, 21)]
    // Past the text is clamped to the last line.
    [InlineData(9, 1, null, null, 23, 26)]
    public void GetRange_marks_the_reported_text(int line, int? column, int? endLine, int? endColumn, int start, int end)
    {
        const string Text = "one\r\n    word(x) + y;\r\nend";

        var range = ExternalToolDiagnostics.GetRange(
            Text,
            new ExternalToolDiagnostic(null, line, column, endLine, endColumn, DiagnosticSeverity.Error, "m"));

        Assert.Equal(TextRange.FromBounds(start, end), range);
    }
}
