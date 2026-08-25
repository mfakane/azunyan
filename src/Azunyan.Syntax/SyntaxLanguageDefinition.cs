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
        IEnumerable<ISyntaxProvider> sources)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(displayName);
        ArgumentNullException.ThrowIfNull(fileExtensions);
        ArgumentNullException.ThrowIfNull(sources);
        Id = id;
        DisplayName = displayName;
        FileExtensions = fileExtensions.Select(NormalizeExtension).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _provider = new CompositeSyntaxProvider(sources);
    }

    public string Id { get; }

    public string DisplayName { get; }

    public IReadOnlyList<string> FileExtensions { get; }

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
}
