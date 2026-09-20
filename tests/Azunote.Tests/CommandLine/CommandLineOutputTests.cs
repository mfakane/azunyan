using System.Text.Json;
using Xunit;

namespace Azunote.Tests.CommandLine;

public sealed class CommandLineOutputTests
{
    private static readonly CommandLineDocumentOutput Document = new(
        "一行目\r\n二行目",
        "二行目",
        @"C:\notes\日誌.md",
        IsDirty: false);

    [Fact]
    public void One_value_is_written_as_the_text_it_names()
    {
        Assert.Equal(
            Document.Text,
            CommandLineOutput.Create([CommandLineOutputTarget.Document], json: false, Document));
        Assert.Equal(
            Document.FilePath,
            CommandLineOutput.Create([CommandLineOutputTarget.FilePath], json: false, Document));
    }

    [Fact]
    public void Several_values_are_written_as_one_line_of_json()
    {
        var payload = CommandLineOutput.Create(
            [CommandLineOutputTarget.FilePath, CommandLineOutputTarget.Document],
            json: true,
            Document);

        // A shell hands native output on as lines, so the object has to
        // survive being read as one.
        Assert.DoesNotContain('\n', payload);
        Assert.DoesNotContain('\r', payload);

        using var parsed = JsonDocument.Parse(payload);
        Assert.Equal(Document.FilePath, parsed.RootElement.GetProperty("filePath").GetString());
        Assert.Equal(Document.Text, parsed.RootElement.GetProperty("document").GetString());
        Assert.Equal(
            ["filePath", "document"],
            parsed.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public void Json_leaves_the_documents_own_characters_readable()
    {
        var payload = CommandLineOutput.Create(
            [CommandLineOutputTarget.Selection],
            json: true,
            Document);

        Assert.Equal("""{"selection":"二行目"}""", payload);
    }

    [Fact]
    public void An_untitled_document_reports_an_empty_file_path()
    {
        var untitled = Document with { FilePath = null };

        Assert.Equal(
            string.Empty,
            CommandLineOutput.Create([CommandLineOutputTarget.FilePath], json: false, untitled));
        Assert.Equal(
            """{"filePath":""}""",
            CommandLineOutput.Create([CommandLineOutputTarget.FilePath], json: true, untitled));
    }

    [Fact]
    public void No_value_writes_nothing()
    {
        Assert.Equal(string.Empty, CommandLineOutput.Create([], json: false, Document));
    }
}
