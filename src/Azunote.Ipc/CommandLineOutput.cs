using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Azunote;

/// <summary>
/// The document state captured when a window closes, used to answer a
/// command line that asked for output through <c>--output</c>.
/// </summary>
public sealed record CommandLineDocumentOutput(
    string Text,
    string SelectedText,
    string? FilePath,
    bool IsDirty);

/// <summary>
/// The vocabulary of <c>--output</c> and the payload it produces. One value is
/// written as the text it names, which is what lets a caller pipe a document
/// around unchanged. More than one value has no such form, so it is written as
/// a JSON object keyed by the same names.
/// </summary>
public static class CommandLineOutput
{
    /// <summary>
    /// The value that asks for no output at all. It stands for an empty list
    /// rather than for a target of its own.
    /// </summary>
    public const string NoneName = "none";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        // The payload is read by a shell, not by a browser, so the readable
        // escaping is the useful one. It leaves the document's own characters
        // alone; a control character such as a line ending stays escaped
        // either way, which is what keeps the object on one line.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false
    };

    /// <summary>
    /// Every value <c>--output</c> accepts, in the order the help and the
    /// shell completion offer them.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        NoneName,
        "filePath",
        "document",
        "selection"
    ];

    public static string GetName(CommandLineOutputTarget target) =>
        target switch
        {
            CommandLineOutputTarget.FilePath => "filePath",
            CommandLineOutputTarget.Document => "document",
            CommandLineOutputTarget.Selection => "selection",
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };

    public static bool TryParseName(string name, out CommandLineOutputTarget target)
    {
        switch (name)
        {
            case "filePath":
                target = CommandLineOutputTarget.FilePath;
                return true;
            case "document":
                target = CommandLineOutputTarget.Document;
                return true;
            case "selection":
                target = CommandLineOutputTarget.Selection;
                return true;
            default:
                target = default;
                return false;
        }
    }

    /// <summary>
    /// Builds what the console client writes to standard output. A JSON object
    /// carries each value under its own name, and every field holds exactly
    /// what the single-value form would have written, so an untitled document
    /// reports an empty file path rather than a missing one.
    /// </summary>
    public static string Create(
        IReadOnlyList<CommandLineOutputTarget> targets,
        bool json,
        CommandLineDocumentOutput document)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(document);

        if (targets.Count == 0)
        {
            return string.Empty;
        }

        if (!json)
        {
            return Read(document, targets[0]);
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            foreach (var target in targets)
            {
                writer.WriteString(GetName(target), Read(document, target));
            }

            writer.WriteEndObject();
        }

        return Utf8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static string Read(CommandLineDocumentOutput document, CommandLineOutputTarget target) =>
        target switch
        {
            CommandLineOutputTarget.FilePath => document.FilePath ?? string.Empty,
            CommandLineOutputTarget.Document => document.Text,
            CommandLineOutputTarget.Selection => document.SelectedText,
            _ => string.Empty
        };
}
