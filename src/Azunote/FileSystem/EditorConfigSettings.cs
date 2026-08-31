using Azunyan.Core;

namespace Azunote;

/// <summary>
/// The subset of EditorConfig properties that affects the current document.
/// Null values mean that the corresponding property was not configured.
/// </summary>
public sealed record EditorConfigSettings(
    IndentationInputMode? IndentationInputMode = null,
    int? IndentSize = null,
    int? TabWidth = null,
    LineEndingKind? LineEnding = null,
    TextEncodingKind? Encoding = null,
    bool? InsertFinalNewline = null,
    bool? TrimTrailingWhitespace = null)
{
    public static EditorConfigSettings Empty { get; } = new();

    public int GetEffectiveTabWidth() =>
        TabWidth ?? IndentSize ?? 4;

    public EditorConfigSettings WithExplicitFileFormat(
        TextEncodingKind encoding,
        LineEndingKind lineEnding) =>
        this with
        {
            Encoding = encoding,
            LineEnding = lineEnding
        };
}

internal static class EditorConfigTextNormalizer
{
    public static string NormalizeForSave(
        string text,
        EditorConfigSettings? settings,
        LineEndingKind lineEnding)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (settings is null)
        {
            return text;
        }

        var normalized = settings.TrimTrailingWhitespace == true
            ? TrimTrailingWhitespace(text)
            : text;
        if (settings.InsertFinalNewline == true
            && normalized.Length > 0
            && normalized[^1] is not '\r' and not '\n')
        {
            normalized += GetLineEnding(lineEnding);
        }

        return normalized;
    }

    private static string TrimTrailingWhitespace(string text)
    {
        var output = new System.Text.StringBuilder(text.Length);
        var pendingWhitespace = new System.Text.StringBuilder();
        foreach (var character in text)
        {
            if (character is ' ' or '\t')
            {
                pendingWhitespace.Append(character);
                continue;
            }

            if (character is '\r' or '\n')
            {
                pendingWhitespace.Clear();
            }
            else if (pendingWhitespace.Length > 0)
            {
                output.Append(pendingWhitespace);
                pendingWhitespace.Clear();
            }

            output.Append(character);
        }

        // Whitespace at EOF is trailing whitespace too.
        return output.ToString();
    }

    private static string GetLineEnding(LineEndingKind lineEnding) => lineEnding switch
    {
        LineEndingKind.Lf => "\n",
        LineEndingKind.CrLf => "\r\n",
        LineEndingKind.Cr => "\r",
        _ => DocumentSession.GetDefaultLineEnding() == LineEndingKind.CrLf
            ? "\r\n"
            : "\n"
    };
}
