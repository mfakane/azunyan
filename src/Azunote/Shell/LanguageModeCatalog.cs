using Azunyan.Core;
using Azunyan.Syntax;

namespace Azunote;

public sealed record LanguageModeEntry(
    string Id,
    string DisplayName,
    ISyntaxProvider? Provider,
    IReadOnlyList<string> CompletionTriggers,
    IReadOnlyList<string> FileExtensions,
    IReadOnlyList<string> Patterns,
    string? DefinitionPath,
    IFoldingProvider? FoldingProvider = null);

/// <summary>
/// Application-owned language mode definitions. It contains no menu or other
/// WinUI objects, so selection and dialog-filter behavior can be tested alone.
/// </summary>
public sealed class LanguageModeCatalog
{
    private readonly IReadOnlyList<LanguageModeEntry> _entries;
    private readonly Dictionary<string, LanguageModeEntry> _byId;
    private readonly int _customModeStartIndex;

    private LanguageModeCatalog(IReadOnlyList<LanguageModeEntry> entries, int customModeStartIndex)
    {
        _entries = entries;
        _customModeStartIndex = customModeStartIndex;
        _byId = entries.ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<LanguageModeEntry> Entries => _entries;

    public int CustomModeStartIndex => _customModeStartIndex;

    public bool TryGet(string id, out LanguageModeEntry entry) => _byId.TryGetValue(id, out entry!);

    public string SelectForPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var selectedId = "plain-text";
        var bestScore = -1;
        foreach (var entry in _entries)
        {
            var score = SyntaxLanguageDefinition.GetPatternMatchScore(path, entry.Patterns);
            if (score > bestScore)
            {
                bestScore = score;
                selectedId = entry.Id;
            }
        }

        return selectedId;
    }

    /// <summary>
    /// Reports whether a path matches one of the language modes, which is how
    /// Azunote decides that a linked file is one of its own documents rather
    /// than something for the Windows default handler.
    /// </summary>
    public bool IsKnownFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _entries.Any(entry =>
            SyntaxLanguageDefinition.GetPatternMatchScore(path, entry.Patterns) >= 0);
    }

    public IReadOnlyList<FileDialogFilter> GetFileDialogFilters()
    {
        var modeFilters = _entries
            .Select(entry => new FileDialogFilter(
                entry.Id,
                entry.DisplayName,
                NormalizeFileExtensions(entry.FileExtensions)))
            .ToArray();
        var supportedExtensions = NormalizeFileExtensions(
            modeFilters.SelectMany(filter => filter.Extensions));

        return
        [
            new FileDialogFilter("supported", "Supported files", supportedExtensions),
            ..modeFilters,
            new FileDialogFilter("all", "All files", ["*"])
        ];
    }

    public static LanguageModeCatalog Create(
        IReadOnlyList<SyntaxLanguageDefinition>? customModes = null)
    {
        var entries = new List<LanguageModeEntry>
        {
            new(
                "plain-text",
                "Plain Text",
                null,
                [],
                [".txt", ".log"],
                ["*.txt", "*.log"],
                null),
            new(
                "azunote",
                "Azunote",
                new AzunoteSyntaxProvider(),
                [".", "(", "{", "[", "->"],
                [".toml"],
                AzunoteLanguageDefinition.Patterns,
                null,
                new AzunoteFoldingProvider())
        };

        entries.AddRange(BuiltInSyntaxLanguages.All.Select(CreateEntry));
        var customModeStartIndex = entries.Count;
        if (customModes is not null)
        {
            var knownIds = entries
                .Select(entry => entry.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            entries.AddRange(
                customModes
                    .Where(mode => knownIds.Add(mode.Id))
                    .Select(CreateEntry));
        }

        return new LanguageModeCatalog(entries, customModeStartIndex);
    }

    private static LanguageModeEntry CreateEntry(SyntaxLanguageDefinition definition) =>
        new(
            definition.Id,
            definition.DisplayName,
            definition,
            definition.CompletionTriggerCharacters,
            definition.FileExtensions,
            definition.Patterns,
            definition.DefinitionPath,
            definition.FoldingProvider);

    private static string[] NormalizeFileExtensions(IEnumerable<string> extensions) =>
        extensions
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Select(extension => extension.Trim())
            .Select(extension => extension is "*" or "*.*"
                ? "*"
                : extension.StartsWith('*')
                    ? NormalizeFileExtension(extension[1..])
                    : NormalizeFileExtension(extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string NormalizeFileExtension(string extension) =>
        extension.StartsWith('.') ? extension : $".{extension}";
}
