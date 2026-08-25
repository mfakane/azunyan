using Azunyan.Core;

namespace Azunyan.Syntax;

/// <summary>An immutable, application-selectable syntax definition.</summary>
public sealed class SyntaxLanguageDefinition : ISyntaxProvider
{
    private readonly CompositeSyntaxProvider _provider;

    public SyntaxLanguageDefinition(
        string id,
        string displayName,
        IEnumerable<string> fileExtensions,
        IEnumerable<ISyntaxProvider> sources,
        IEnumerable<string>? completionTriggerCharacters = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(displayName);
        ArgumentNullException.ThrowIfNull(fileExtensions);
        ArgumentNullException.ThrowIfNull(sources);
        Id = id;
        DisplayName = displayName;
        FileExtensions = fileExtensions.Select(NormalizeExtension).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        CompletionTriggerCharacters = (completionTriggerCharacters ?? Array.Empty<string>())
            .Select(NormalizeTrigger)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        _provider = new CompositeSyntaxProvider(sources);
    }

    public string Id { get; }

    public string DisplayName { get; }

    public IReadOnlyList<string> FileExtensions { get; }

    /// <summary>
    /// Literal strings which cause the host to request completion after they
    /// are inserted. Multi-character triggers such as <c>-&gt;</c> are allowed.
    /// </summary>
    public IReadOnlyList<string> CompletionTriggerCharacters { get; }

    public IReadOnlyList<ISyntaxProvider> Sources => _provider.Sources;

    public ValueTask<IReadOnlyList<SyntaxSpan>> GetSyntaxAsync(
        EditorProviderContext context,
        CancellationToken cancellationToken = default) =>
        _provider.GetSyntaxAsync(context, cancellationToken);

    private static string NormalizeExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrEmpty(extension);
        return extension[0] == '.' ? extension : $".{extension}";
    }

    private static string NormalizeTrigger(string trigger)
    {
        ArgumentException.ThrowIfNullOrEmpty(trigger);
        return trigger;
    }
}
