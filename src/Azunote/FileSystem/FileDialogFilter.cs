namespace Azunote;

/// <summary>
/// One file-type entry shown by the native Open and Save dialogs.
/// </summary>
public sealed record FileDialogFilter(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Extensions)
{
    public string Specification => Extensions.Count == 0
        ? "*.*"
        : string.Join(';', Extensions.Select(extension =>
            extension is "*" or "*.*"
                ? "*.*"
                : extension.StartsWith('*')
                    ? extension
                    : $"*{(extension.StartsWith('.') ? extension : $".{extension}")}"));
}
