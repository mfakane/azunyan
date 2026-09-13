namespace Azunyan.Core;

/// <summary>
/// Completes words already present in the current document. The provider is
/// intentionally language-neutral, so it can be used as a fallback when a
/// language does not have a semantic completion service.
/// </summary>
public sealed class DocumentWordCompletionProvider : ICompletionProvider
{
    private const int MaximumCandidateCount = 100;

    public ValueTask<CompletionResult?> GetCompletionsAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var text = context.Snapshot.Text;
        var replacementStart = FindWordStart(text, context.Position);
        var prefix = text[replacementStart..context.Position];
        if (prefix.Length == 0)
        {
            return ValueTask.FromResult<CompletionResult?>(null);
        }

        var words = CollectWords(text, cancellationToken);
        var candidates = words
            .Where(word => word.Value.Text.Length > prefix.Length
                && word.Value.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(word =>
                word.Value.Text.StartsWith(prefix, StringComparison.Ordinal)
                    ? 1
                    : 0)
            .ThenByDescending(word => word.Value.Count)
            .ThenBy(word => word.Value.FirstPosition)
            .Take(MaximumCandidateCount)
            .Select((word, index) => new CompletionItem(
                word.Value.Text,
                detail: "Document word",
                sortOrder: index))
            .ToArray();

        return candidates.Length == 0
            ? ValueTask.FromResult<CompletionResult?>(null)
            : ValueTask.FromResult<CompletionResult?>(new CompletionResult(
                TextRange.FromBounds(replacementStart, context.Position),
                candidates));
    }

    private static Dictionary<string, WordInfo> CollectWords(
        string text,
        CancellationToken cancellationToken)
    {
        var words = new Dictionary<string, WordInfo>(StringComparer.OrdinalIgnoreCase);
        var position = 0;
        while (position < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsWordPart(text[position]))
            {
                position++;
                continue;
            }

            var start = position++;
            while (position < text.Length && IsWordPart(text[position]))
            {
                position++;
            }

            var word = text[start..position];
            var key = word;
            if (words.TryGetValue(key, out var existing))
            {
                words[key] = existing with { Count = existing.Count + 1 };
            }
            else
            {
                words.Add(key, new WordInfo(word, 1, start));
            }
        }

        return words;
    }

    private static int FindWordStart(string text, int position)
    {
        var start = position;
        while (start > 0 && IsWordPart(text[start - 1]))
        {
            start--;
        }

        return start;
    }

    private static bool IsWordPart(char value)
    {
        if (char.IsLetterOrDigit(value) || char.IsSurrogate(value))
        {
            return true;
        }

        return char.GetUnicodeCategory(value) is
            System.Globalization.UnicodeCategory.NonSpacingMark or
            System.Globalization.UnicodeCategory.SpacingCombiningMark or
            System.Globalization.UnicodeCategory.ConnectorPunctuation;
    }

    private sealed record WordInfo(string Text, int Count, int FirstPosition);
}
