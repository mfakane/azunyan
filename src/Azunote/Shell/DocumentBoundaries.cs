using Azunyan.Core;

namespace Azunote;

public interface ITextFileStore
{
    Task<TextFileData> ReadAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<TextFileData> ReadAsync(
        string path,
        TextEncodingKind? encodingHint,
        CancellationToken cancellationToken = default);

    Task WriteAsync(
        string path,
        string text,
        TextEncodingKind encoding,
        LineEndingKind lineEnding,
        CancellationToken cancellationToken = default);
}

public sealed class TextFileStore : ITextFileStore
{
    public Task<TextFileData> ReadAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        TextFileService.ReadAsync(path, cancellationToken);

    public Task<TextFileData> ReadAsync(
        string path,
        TextEncodingKind? encodingHint,
        CancellationToken cancellationToken = default) =>
        TextFileService.ReadAsync(path, encodingHint, cancellationToken);

    public Task WriteAsync(
        string path,
        string text,
        TextEncodingKind encoding,
        LineEndingKind lineEnding,
        CancellationToken cancellationToken = default) =>
        TextFileService.WriteAsync(path, text, encoding, lineEnding, cancellationToken);
}

/// <summary>
/// The small editor surface needed by application workflows. It deliberately
/// exposes Core value types instead of WinUI controls.
/// </summary>
public interface IEditorBuffer
{
    string Text { get; }

    string SelectedText { get; }

    TextSnapshot Snapshot { get; }

    TextSelection Selection { get; }

    int CaretPosition { get; }

    void SetText(string text);

    void SetSelection(TextSelection selection);

    void Replace(TextRange range, string replacement);
}

public enum PendingChangesDecision
{
    Save,
    Discard,
    Cancel
}

public enum ExternalChangeDecision
{
    Reload,
    Keep,
    Cancel
}

public interface IUserPrompt
{
    Task<PendingChangesDecision> ConfirmPendingChangesAsync();

    Task<ExternalChangeDecision> ResolveExternalChangeAsync();

    Task ShowErrorAsync(string title, string message);
}
