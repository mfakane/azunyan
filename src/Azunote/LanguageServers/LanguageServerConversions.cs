using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Azunyan.Core;

namespace Azunote;

/// <summary>Turns what a language server answers into Azunote's editor types.</summary>
internal static partial class LanguageServerConversions
{
    private const int MaximumCompletionCount = 200;

    [GeneratedRegex(@"\$\{\d+:([^}]*)\}|\$\{\d+\|([^,}]*)[^}]*\}|\$\{\d+\}|\$\d+", RegexOptions.CultureInvariant)]
    private static partial Regex SnippetPlaceholderPattern();

    /// <summary>The offset of an LSP position, clamped into the text.</summary>
    public static int ToOffset(TextSnapshot snapshot, JsonNode? position)
    {
        var lines = snapshot.Lines;
        var line = Math.Clamp(GetInt(position?["line"]), 0, lines.LineCount - 1);
        var character = Math.Clamp(GetInt(position?["character"]), 0, lines.GetLineLength(line));
        return lines.GetLineStart(line) + character;
    }

    public static TextRange ToRange(TextSnapshot snapshot, JsonNode? range)
    {
        var start = ToOffset(snapshot, range?["start"]);
        var end = ToOffset(snapshot, range?["end"]);
        return TextRange.FromBounds(start, Math.Max(start, end));
    }

    public static IReadOnlyList<DocumentDiagnostic> ToDiagnostics(
        JsonArray diagnostics,
        TextSnapshot snapshot,
        string sourceKey,
        string defaultSource)
    {
        var text = snapshot.Text;
        var result = new List<DocumentDiagnostic>(diagnostics.Count);
        foreach (var diagnostic in diagnostics.OfType<JsonObject>())
        {
            if (GetString(diagnostic["message"]) is not { Length: > 0 } message)
            {
                continue;
            }

            var range = ToRange(snapshot, diagnostic["range"]);
            if (range.Length == 0)
            {
                range = GetWordOrCharacter(text, range.Start);
            }

            var severity = GetInt(diagnostic["severity"], 1) switch
            {
                2 => DiagnosticSeverity.Warning,
                3 or 4 => DiagnosticSeverity.Information,
                _ => DiagnosticSeverity.Error
            };
            result.Add(new DocumentDiagnostic(
                sourceKey,
                GetString(diagnostic["source"]) ?? defaultSource,
                range,
                severity,
                message));
        }

        return result;
    }

    /// <summary>
    /// The server's items that match the word before the caret. The editor
    /// replaces one range for every item, so an item that would replace more
    /// or less than that word has its text adjusted to the same result.
    /// </summary>
    public static CompletionResult? ToCompletionResult(JsonNode? completion, EditorProviderContext context)
    {
        var items = completion as JsonArray ?? completion?["items"] as JsonArray;
        if (items is null)
        {
            return null;
        }

        var snapshot = context.Snapshot;
        var text = snapshot.Text;
        var position = context.Position;
        var wordStart = position;
        while (wordStart > 0 && IsWordCharacter(text[wordStart - 1]))
        {
            wordStart--;
        }

        var prefix = text[wordStart..position];
        var candidates = new List<(string SortText, int Index, CompletionItem Item)>();
        foreach (var (item, index) in items.OfType<JsonObject>().Select((item, index) => (item, index)))
        {
            if (GetString(item["label"]) is not { Length: > 0 } label)
            {
                continue;
            }

            var filterText = GetString(item["filterText"]) ?? label;
            if (!filterText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && !label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var edit = item["textEdit"];
            var insertText = GetString(edit?["newText"]) ?? GetString(item["insertText"]) ?? label;
            if (GetInt(item["insertTextFormat"], 1) == 2)
            {
                insertText = SnippetPlaceholderPattern().Replace(
                    insertText,
                    match => match.Groups[1].Success ? match.Groups[1].Value
                        : match.Groups[2].Success ? match.Groups[2].Value
                        : string.Empty);
            }

            if ((edit?["range"] ?? edit?["insert"]) is { } editRange)
            {
                var editStart = Math.Min(ToOffset(snapshot, editRange["start"]), position);
                if (editStart < wordStart)
                {
                    var replaced = text[editStart..wordStart];
                    insertText = insertText.StartsWith(replaced, StringComparison.Ordinal)
                        ? insertText[replaced.Length..]
                        : insertText;
                }
                else if (editStart > wordStart)
                {
                    insertText = text[wordStart..editStart] + insertText;
                }
            }

            candidates.Add((
                GetString(item["sortText"]) ?? label,
                index,
                new CompletionItem(
                    label,
                    insertText,
                    GetString(item["detail"]),
                    GetMarkupText(item["documentation"]))));
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var sorted = candidates
            .OrderBy(candidate => candidate.SortText, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Index)
            .Take(MaximumCompletionCount)
            .Select((candidate, order) => new CompletionItem(
                candidate.Item.Label,
                candidate.Item.InsertText,
                candidate.Item.Detail,
                candidate.Item.Documentation,
                order))
            .ToArray();
        return new CompletionResult(TextRange.FromBounds(wordStart, position), sorted);
    }

    public static TooltipData? ToTooltip(JsonNode? hover, EditorProviderContext context)
    {
        if (hover is not JsonObject result || GetMarkupText(result["contents"]) is not { Length: > 0 } content)
        {
            return null;
        }

        var range = result["range"] is { } resultRange
            ? ToRange(context.Snapshot, resultRange)
            : GetWordOrCharacter(context.Snapshot.Text, context.Position);
        return new TooltipData(range, content);
    }

    /// <summary>
    /// The text of hover contents or documentation: a string, a marked string,
    /// markup content, or an array of them. Markdown code fences are dropped
    /// since the tooltip shows plain text.
    /// </summary>
    internal static string? GetMarkupText(JsonNode? markup)
    {
        var text = markup switch
        {
            JsonArray parts => string.Join(
                Environment.NewLine + Environment.NewLine,
                parts.Select(GetMarkupText).Where(part => !string.IsNullOrEmpty(part))),
            JsonObject content => GetString(content["value"]),
            JsonValue value => GetString(value),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lines = text.ReplaceLineEndings("\n").Split('\n')
            .Where(line => !line.TrimStart().StartsWith("```", StringComparison.Ordinal));
        return string.Join(Environment.NewLine, lines).Trim();
    }

    private static TextRange GetWordOrCharacter(string text, int position)
    {
        var start = position;
        while (start > 0 && IsWordCharacter(text[start - 1]))
        {
            start--;
        }

        var end = position;
        while (end < text.Length && IsWordCharacter(text[end]))
        {
            end++;
        }

        if (start < end)
        {
            return TextRange.FromBounds(start, end);
        }

        return position < text.Length && text[position] is not ('\r' or '\n')
            ? new TextRange(position, 1)
            : position > 0 && text[position - 1] is not ('\r' or '\n')
                ? new TextRange(position - 1, 1)
                : new TextRange(position, 0);
    }

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    private static string? GetString(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    private static int GetInt(JsonNode? node, int fallback = 0) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<int>(out var number)
            ? number
            : fallback;
}
