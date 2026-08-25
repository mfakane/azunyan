using Azunyan.Core;

namespace Azunyan.Syntax;

/// <summary>An immutable, application-selectable syntax definition.</summary>
public sealed class SyntaxLanguageDefinition : ISyntaxProvider
{
    private readonly CompositeSyntaxProvider _provider;

    public SyntaxLanguageDefinition(
        string id,
        string displayName,
        IEnumerable<string> patterns,
        IEnumerable<ISyntaxProvider> sources,
        IEnumerable<string>? completionTriggerCharacters = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(displayName);
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(sources);
        Id = id;
        DisplayName = displayName;
        Patterns = patterns
            .Select(NormalizePattern)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        FileExtensions = Patterns
            .Select(TryGetExtension)
            .Where(extension => extension is not null)
            .Select(extension => extension!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        CompletionTriggerCharacters = (completionTriggerCharacters ?? Array.Empty<string>())
            .Select(NormalizeTrigger)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        _provider = new CompositeSyntaxProvider(sources);
    }

    public string Id { get; }

    public string DisplayName { get; }

    /// <summary>
    /// File-name or path-suffix glob patterns associated with this definition.
    /// Patterns without a path separator match the file name; patterns with a
    /// separator match any suffix of the normalized path.
    /// </summary>
    public IReadOnlyList<string> Patterns { get; }

    /// <summary>
    /// Extensions inferred from <see cref="Patterns"/> for native file dialogs.
    /// </summary>
    public IReadOnlyList<string> FileExtensions { get; }

    /// <summary>
    /// Literal strings which cause the host to request completion after they
    /// are inserted. Multi-character triggers such as <c>-&gt;</c> are allowed.
    /// </summary>
    public IReadOnlyList<string> CompletionTriggerCharacters { get; }

    public IReadOnlyList<ISyntaxProvider> Sources => _provider.Sources;

    public int GetPatternMatchScore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return GetPatternMatchScore(path, Patterns);
    }

    public static int GetPatternMatchScore(
        string path,
        IEnumerable<string> patterns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(patterns);
        var normalizedPath = NormalizePath(path);
        var score = -1;
        foreach (var pattern in patterns)
        {
            var normalizedPattern = NormalizePattern(pattern);
            score = Math.Max(
                score,
                SyntaxPatternMatcher.GetMatchScore(normalizedPath, normalizedPattern));
        }

        return score;
    }

    public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default) =>
        _provider.GetSyntaxAsync(context, cancellationToken);

    private static string NormalizePattern(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        var normalized = NormalizePath(pattern).TrimStart('/');
        if (normalized.Length == 0)
        {
            throw new ArgumentException("A language pattern cannot be empty.", nameof(pattern));
        }

        // Keep accepting the old extension-shaped values while definitions
        // migrate to explicit globs.
        if (!normalized.Contains('*')
            && !normalized.Contains('?')
            && !normalized.Contains('/')
            && normalized.StartsWith(".", StringComparison.Ordinal))
        {
            return $"*{normalized}";
        }

        return normalized;
    }

    private static string NormalizeTrigger(string trigger)
    {
        ArgumentException.ThrowIfNullOrEmpty(trigger);
        return trigger;
    }

    private static string? TryGetExtension(string pattern)
    {
        var fileName = pattern[(pattern.LastIndexOf('/') + 1)..];
        var dot = fileName.LastIndexOf('.');
        if (dot <= 0 || dot == fileName.Length - 1)
        {
            return null;
        }

        var extension = fileName[dot..];
        return extension.Contains('*') || extension.Contains('?')
            ? null
            : extension;
    }

    private static string NormalizePath(string value) =>
        value.Trim().Replace('\\', '/');
}

internal static class SyntaxPatternMatcher
{
    public static int GetMatchScore(string path, string pattern)
    {
        var candidate = pattern.Contains('/', StringComparison.Ordinal)
            ? path
            : path[(path.LastIndexOf('/') + 1)..];
        var score = 0;
        var hasMatch = false;
        for (var start = 0; start <= candidate.Length; start++)
        {
            if (start > 0 && candidate[start - 1] != '/')
            {
                continue;
            }

            if (IsGlobMatch(candidate[start..], pattern))
            {
                hasMatch = true;
                score = Math.Max(score, pattern.Count(char.IsLetterOrDigit) * 4
                    + pattern.Count(character => character == '/') * 8
                    - pattern.Count(character => character is '*' or '?'));
            }
        }

        return hasMatch ? score : -1;
    }

    private static bool IsGlobMatch(string text, string pattern)
    {
        var textIndex = 0;
        var patternIndex = 0;
        var starIndex = -1;
        var starTextIndex = -1;
        while (textIndex < text.Length)
        {
            if (patternIndex < pattern.Length
                && (pattern[patternIndex] == '?'
                    || char.ToUpperInvariant(pattern[patternIndex])
                        == char.ToUpperInvariant(text[textIndex])))
            {
                patternIndex++;
                textIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                starTextIndex = textIndex;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                textIndex = ++starTextIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }
}
