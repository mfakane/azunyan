using Azunyan.Core;
using Azunyan.Syntax;

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
    private static readonly CompositeSyntaxProvider Provider = new(new ISyntaxProvider[]
    {
        new LineRemainderSyntaxRule("#", "heading", requireLineStart: true),
        new LiteralSyntaxRule("[ ]", "task-marker"),
        new LiteralSyntaxRule("[x]", "task-marker", StringComparison.OrdinalIgnoreCase),
        new LineRemainderSyntaxRule("//", "comment"),
        new DelimitedSyntaxRule("\"", "\"", "string", allowLineBreaks: false, escapePrefix: "\\"),
        new DelimitedSyntaxRule("'", "'", "string", allowLineBreaks: false, escapePrefix: "\\"),
        new RegexSyntaxRule(@"\b\d+(?:\.\d+)?\b", "number"),
        new KeywordSyntaxRule(
            AzunoteLanguageDefinition.Keywords,
            comparer: StringComparer.OrdinalIgnoreCase)
    });

    public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        return Provider.GetSyntaxAsync(context, cancellationToken);
    }
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

/// <summary>
/// Supplies small, language-neutral explanations for Azunote's built-in
/// note keywords. Applications can replace this provider with a language
/// service without changing the editor host.
/// </summary>
public sealed class AzunoteTooltipProvider : ITooltipProvider
{
    private static readonly IReadOnlyDictionary<string, string> Descriptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TODO"] = "A task or follow-up item.",
            ["DONE"] = "A completed task marker.",
            ["FIXME"] = "A known issue that needs correction.",
            ["NOTE"] = "An informational note.",
            ["IMPORTANT"] = "A note that should receive extra attention.",
            ["QUESTION"] = "An open question or unresolved point."
        };

    public ValueTask<TooltipData?> GetTooltipAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var text = context.Snapshot.Text;
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

        if (start == end)
        {
            return ValueTask.FromResult<TooltipData?>(null);
        }

        var word = text[start..end];
        return ValueTask.FromResult<TooltipData?>(
            Descriptions.TryGetValue(word, out var description)
                ? new TooltipData(TextRange.FromBounds(start, end), description, word)
                : null);
    }

    private static bool IsIdentifierPart(char value) => char.IsLetterOrDigit(value) || value == '_';
}

/// <summary>
/// Supplies lightweight heading-based folds for Azunote. The editor owns
/// collapsed state; this provider only reports candidate ranges.
/// </summary>
public sealed class AzunoteFoldingProvider : IFoldingProvider
{
    public ValueTask<IReadOnlyList<FoldRange>> GetFoldsAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var snapshot = context.Snapshot;
        var headings = new List<(int Line, int Level, TextRange Range)>();
        for (var line = 0; line < snapshot.Lines.LineCount; line++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var range = snapshot.Lines.GetLineRange(line);
            var text = snapshot.GetText(range);
            var level = GetHeadingLevel(text);
            if (level > 0)
            {
                headings.Add((line, level, range));
            }
        }

        var folds = new List<FoldRange>();
        for (var index = 0; index < headings.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var heading = headings[index];
            var end = snapshot.Length;
            for (var next = index + 1; next < headings.Count; next++)
            {
                if (headings[next].Level <= heading.Level)
                {
                    end = headings[next].Range.Start;
                    break;
                }
            }

            var start = heading.Range.End;
            if (end > start)
            {
                folds.Add(new FoldRange(
                    $"heading:{heading.Line}",
                    TextRange.FromBounds(start, end),
                    " …"));
            }
        }

        return ValueTask.FromResult<IReadOnlyList<FoldRange>>(folds);
    }

    private static int GetHeadingLevel(string line)
    {
        var level = 0;
        while (level < line.Length && line[level] == '#')
        {
            level++;
        }

        return level > 0 && (level == line.Length || char.IsWhiteSpace(line[level]))
            ? level
            : 0;
    }
}
