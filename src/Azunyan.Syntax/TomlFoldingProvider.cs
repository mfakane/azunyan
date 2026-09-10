using Azunyan.Core;

namespace Azunyan.Syntax;

/// <summary>
/// Finds foldable TOML table sections. A section contains the lines after its
/// header and ends immediately before the next table header. Both standard
/// tables (<c>[table]</c>) and arrays of tables (<c>[[table]]</c>) are
/// supported.
/// </summary>
public sealed class TomlFoldingProvider : IFoldingProvider
{
    public ValueTask<IReadOnlyList<FoldRange>> GetFoldsAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var snapshot = context.Snapshot;
        var sections = new List<TomlSection>();
        var multilineDelimiter = string.Empty;

        for (var line = 0; line < snapshot.Lines.LineCount; line++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var range = snapshot.Lines.GetLineRange(line);
            var text = snapshot.GetText(range);
            if (multilineDelimiter.Length == 0
                && TryGetTableHeader(text, out var path, out var isArray))
            {
                sections.Add(new TomlSection(line, path, isArray, range));
            }

            multilineDelimiter = UpdateMultilineStringState(text, multilineDelimiter);
        }

        var folds = new List<FoldRange>(sections.Count);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < sections.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var section = sections[index];
            var nextLine = index + 1 < sections.Count
                ? sections[index + 1].Line
                : snapshot.Lines.LineCount;
            if (!HasBody(snapshot, section.Line + 1, nextLine))
            {
                continue;
            }

            var end = index + 1 < sections.Count
                ? sections[index + 1].Range.Start
                : snapshot.Length;
            var start = section.Range.End;
            if (end <= start)
            {
                continue;
            }

            var occurrenceKey = (section.IsArray ? "array:" : "table:") + section.Path;
            occurrences.TryGetValue(occurrenceKey, out var occurrence);
            occurrences[occurrenceKey] = occurrence + 1;
            folds.Add(new FoldRange(
                $"toml-section:{occurrenceKey}:{occurrence}",
                TextRange.FromBounds(start, end),
                " …"));
        }

        return ValueTask.FromResult<IReadOnlyList<FoldRange>>(folds);
    }

    private static bool HasBody(TextSnapshot snapshot, int firstLine, int endLine)
    {
        for (var line = firstLine; line < endLine; line++)
        {
            if (!string.IsNullOrWhiteSpace(
                    snapshot.GetText(snapshot.Lines.GetLineRange(line))))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetTableHeader(
        string line,
        out string path,
        out bool isArray)
    {
        path = string.Empty;
        isArray = false;
        var start = 0;
        SkipHorizontalWhitespace(line, ref start);
        if (start >= line.Length || line[start] != '[')
        {
            return false;
        }

        isArray = start + 1 < line.Length && line[start + 1] == '[';
        var contentStart = start + (isArray ? 2 : 1);
        var closing = FindClosingBracket(line, contentStart, isArray);
        if (closing < 0)
        {
            return false;
        }

        var after = closing + (isArray ? 2 : 1);
        SkipHorizontalWhitespace(line, ref after);
        if (after < line.Length && line[after] != '#')
        {
            return false;
        }

        path = line[contentStart..closing].Trim();
        return path.Length > 0;
    }

    private static int FindClosingBracket(string line, int start, bool isArray)
    {
        var quote = '\0';
        var escaped = false;
        for (var index = start; index < line.Length; index++)
        {
            var character = line[index];
            if (quote != '\0')
            {
                if (quote == '"' && escaped)
                {
                    escaped = false;
                }
                else if (quote == '"' && character == '\\')
                {
                    escaped = true;
                }
                else if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                continue;
            }

            if (character != ']')
            {
                continue;
            }

            if (!isArray || index + 1 < line.Length && line[index + 1] == ']')
            {
                return index;
            }
        }

        return -1;
    }

    private static string UpdateMultilineStringState(string line, string delimiter)
    {
        var activeDelimiter = delimiter;
        var index = 0;
        while (index < line.Length)
        {
            if (activeDelimiter.Length > 0)
            {
                var close = line.IndexOf(activeDelimiter, index, StringComparison.Ordinal);
                if (close < 0)
                {
                    return activeDelimiter;
                }

                activeDelimiter = string.Empty;
                index = close + 3;
                continue;
            }

            if (line[index] == '#')
            {
                break;
            }

            if (index + 2 < line.Length
                && line.AsSpan(index, 3).SequenceEqual("\"\"\""))
            {
                activeDelimiter = "\"\"\"";
                index += 3;
                continue;
            }

            if (index + 2 < line.Length
                && line.AsSpan(index, 3).SequenceEqual("'''"))
            {
                activeDelimiter = "'''";
                index += 3;
                continue;
            }

            if (line[index] is '"' or '\'')
            {
                var quote = line[index++];
                var escaped = false;
                while (index < line.Length)
                {
                    var character = line[index++];
                    if (quote == '"' && escaped)
                    {
                        escaped = false;
                    }
                    else if (quote == '"' && character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == quote)
                    {
                        break;
                    }
                }

                continue;
            }

            index++;
        }

        return activeDelimiter;
    }

    private static void SkipHorizontalWhitespace(string line, ref int index)
    {
        while (index < line.Length && line[index] is ' ' or '\t')
        {
            index++;
        }
    }

    private readonly record struct TomlSection(
        int Line,
        string Path,
        bool IsArray,
        TextRange Range);
}
