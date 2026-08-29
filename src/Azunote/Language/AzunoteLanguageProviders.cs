using Azunyan.Core;
using Azunyan.Syntax;

namespace Azunote;

/// <summary>Language metadata for Azunote-owned configuration files.</summary>
public static class AzunoteLanguageDefinition
{
    public static IReadOnlyList<string> Patterns => AzunoteSchemaCatalog.Patterns;
}

/// <summary>
/// TOML syntax used by Azunote's configuration language mode. Configuration
/// semantics are supplied separately by the schema-backed completion provider.
/// </summary>
public sealed class AzunoteSyntaxProvider : IIncrementalSyntaxProvider
{
    private static readonly SyntaxLanguageDefinition Provider = BuiltInSyntaxLanguages.Toml;

    public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        return Provider.GetSyntaxAsync(context, cancellationToken);
    }

    public ValueTask<SyntaxAnalysis> GetSyntaxAnalysisAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default) =>
        Provider.GetSyntaxAnalysisAsync(context, cancellationToken);

    public ValueTask<SyntaxAnalysis> GetSyntaxAsync(
        EditorProviderContext context,
        TextSnapshot previousSnapshot,
        TextChange change,
        SyntaxAnalysis previousAnalysis,
        CancellationToken cancellationToken = default) =>
        Provider.GetSyntaxAsync(
            context,
            previousSnapshot,
            change,
            previousAnalysis,
            cancellationToken);
}

/// <summary>Compatibility name for the schema-backed configuration provider.</summary>
public sealed class AzunoteCompletionProvider : ICompletionProvider
{
    private readonly AzunoteConfigurationCompletionProvider _provider;

    public AzunoteCompletionProvider(
        IReadOnlyList<AzunoteSchemaDefinition>? schemas = null)
    {
        _provider = new AzunoteConfigurationCompletionProvider(schemas);
    }

    public ValueTask<CompletionResult?> GetCompletionsAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
        => _provider.GetCompletionsAsync(context, cancellationToken);
}

/// <summary>
/// Supplies small, language-neutral explanations for Azunote's built-in
/// note keywords. Applications can replace this provider with a language
/// service without changing the editor host.
/// </summary>
public sealed class AzunoteTooltipProvider : ITooltipProvider
{
    private static readonly Dictionary<string, string> Descriptions =
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
