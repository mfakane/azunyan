using Azunyan.Core;

namespace Azunote;

/// <summary>
/// The intentionally small vocabulary understood by the built-in Azunote
/// providers. More capable language services can be supplied by applications
/// through the provider interfaces without changing this list.
/// </summary>
public static class AzunoteLanguageDefinition
{
    public static IReadOnlyList<string> Keywords { get; } = new[]
    {
        "TODO",
        "DONE",
        "FIXME",
        "NOTE",
        "IMPORTANT",
        "QUESTION"
    };
}

/// <summary>
/// A lightweight note syntax provider. It recognizes headings, task markers,
/// note keywords, quoted strings, numbers, and // comments. It is deliberately
/// lexical and does not pretend to be a parser for an external language.
/// </summary>
public sealed class AzunoteSyntaxProvider : ISyntaxProvider
{
    private static readonly HashSet<string> Keywords =
        new(AzunoteLanguageDefinition.Keywords, StringComparer.OrdinalIgnoreCase);

    public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var spans = new List<SyntaxSpan>();
        var text = context.Snapshot.Text;
        var position = 0;

        while (position < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (text[position] == '#' && IsLineStart(text, position))
            {
                var lineEnd = FindLineEnd(text, position);
                spans.Add(new SyntaxSpan(TextRange.FromBounds(position, lineEnd), "heading"));
                position = lineEnd;
                continue;
            }

            if (text[position] == '[' && position + 2 < text.Length
                && (text[position + 1] == ' ' || text[position + 1] is 'x' or 'X')
                && text[position + 2] == ']')
            {
                spans.Add(new SyntaxSpan(new TextRange(position, 3), "task-marker"));
                position += 3;
                continue;
            }

            if (text[position] == '/' && position + 1 < text.Length && text[position + 1] == '/')
            {
                var lineEnd = FindLineEnd(text, position);
                spans.Add(new SyntaxSpan(TextRange.FromBounds(position, lineEnd), "comment"));
                position = lineEnd;
                continue;
            }

            if (text[position] is '\'' or '"')
            {
                var start = position;
                position = ConsumeQuoted(text, position, cancellationToken);
                spans.Add(new SyntaxSpan(TextRange.FromBounds(start, position), "string"));
                continue;
            }

            if (char.IsDigit(text[position]))
            {
                var start = position++;
                while (position < text.Length && (char.IsDigit(text[position]) || text[position] == '.'))
                {
                    position++;
                }

                spans.Add(new SyntaxSpan(TextRange.FromBounds(start, position), "number"));
                continue;
            }

            if (IsIdentifierPart(text[position]))
            {
                var start = position++;
                while (position < text.Length && IsIdentifierPart(text[position]))
                {
                    position++;
                }

                var word = text[start..position];
                if (Keywords.Contains(word))
                {
                    spans.Add(new SyntaxSpan(TextRange.FromBounds(start, position), "keyword"));
                }

                continue;
            }

            position++;
        }

        return ValueTask.FromResult<IReadOnlyList<SyntaxSpan>>(spans);
    }

    private static int ConsumeQuoted(string text, int start, CancellationToken cancellationToken)
    {
        var quote = text[start];
        var position = start + 1;
        while (position < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (text[position] == '\\')
            {
                position = Math.Min(text.Length, position + 2);
                continue;
            }

            if (text[position++] == quote)
            {
                break;
            }
        }

        return position;
    }

    private static bool IsLineStart(string text, int position) =>
        position == 0 || text[position - 1] is '\r' or '\n';

    private static int FindLineEnd(string text, int position)
    {
        while (position < text.Length && text[position] is not '\r' and not '\n')
        {
            position++;
        }

        return position;
    }

    private static bool IsIdentifierPart(char value) => char.IsLetterOrDigit(value) || value == '_';
}

/// <summary>
/// Supplies completions from Azunote's small keyword vocabulary and distinct
/// identifier-like words already present in the current snapshot.
/// </summary>
public sealed class AzunoteCompletionProvider : ICompletionProvider
{
    public ValueTask<CompletionResult?> GetCompletionsAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var snapshot = context.Snapshot;
        var text = snapshot.Text;
        var start = context.Position;
        while (start > 0 && IsIdentifierPart(text[start - 1]))
        {
            start--;
        }

        var end = context.Position;
        while (end < text.Length && IsIdentifierPart(text[end]))
        {
            end++;
        }

        var prefix = text[start..context.Position];
        var candidates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var keyword in AzunoteLanguageDefinition.Keywords)
        {
            candidates.TryAdd(keyword, 0);
        }

        var wordStart = 0;
        while (wordStart < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (wordStart < text.Length && !IsIdentifierPart(text[wordStart]))
            {
                wordStart++;
            }

            var wordEnd = wordStart;
            while (wordEnd < text.Length && IsIdentifierPart(text[wordEnd]))
            {
                wordEnd++;
            }

            if (wordEnd > wordStart)
            {
                candidates.TryAdd(text[wordStart..wordEnd], 1);
            }

            wordStart = wordEnd;
        }

        var items = candidates
            .Where(pair => prefix.Length == 0 || pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new CompletionItem(pair.Key, sortOrder: pair.Value))
            .ToArray();

        return ValueTask.FromResult<CompletionResult?>(
            new CompletionResult(TextRange.FromBounds(start, end), items));
    }

    private static bool IsIdentifierPart(char value) => char.IsLetterOrDigit(value) || value == '_';
}
