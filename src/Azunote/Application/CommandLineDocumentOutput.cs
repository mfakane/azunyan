namespace Azunote;

/// <summary>
/// The document state captured when a window closes, used to answer a
/// command line that asked for output through <c>--output</c>.
/// </summary>
internal sealed record CommandLineDocumentOutput(
    string Text,
    string SelectedText,
    string? FilePath,
    bool IsDirty);
