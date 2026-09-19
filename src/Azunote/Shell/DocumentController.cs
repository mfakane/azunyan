using Azunyan.Core;

namespace Azunote;

/// <summary>
/// Coordinates document replacement and persistence without depending on
/// WinUI. Dialogs and file pickers are supplied through small boundaries.
/// </summary>
public sealed class DocumentController
{
    private readonly IEditorBuffer _editor;
    private readonly DocumentSession _session;
    private readonly ITextFileStore _files;
    private readonly IUserPrompt _prompt;

    public DocumentController(
        IEditorBuffer editor,
        DocumentSession session,
        ITextFileStore files,
        IUserPrompt prompt)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
    }

    public DocumentSession Session => _session;

    public Task<TextFileData> ReadAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        _files.ReadAsync(path, cancellationToken);

    public Task<TextFileData> ReadAsync(
        string path,
        TextEncodingKind? encodingHint,
        CancellationToken cancellationToken = default) =>
        _files.ReadAsync(path, encodingHint, cancellationToken);

    public async Task<bool> ConfirmPendingChangesAsync(Func<Task<bool>> saveAsync)
    {
        ArgumentNullException.ThrowIfNull(saveAsync);
        if (!_session.State.IsDirty)
        {
            return true;
        }

        return await _prompt.ConfirmPendingChangesAsync() switch
        {
            PendingChangesDecision.Save => await saveAsync(),
            PendingChangesDecision.Discard => true,
            _ => false
        };
    }

    public async Task OpenAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await OpenAsync(
            path,
            encodingHint: null,
            cancellationToken: cancellationToken);
    }

    public async Task OpenAsync(
        string path,
        TextEncodingKind? encodingHint,
        CancellationToken cancellationToken = default)
    {
        var data = await _files.ReadAsync(path, encodingHint, cancellationToken);
        ReplaceEditorText(data.Text);
        _session.Load(path, data, BundledLegalDocuments.IsReadOnlyPath(path));
        _editor.SetSelection(TextSelection.Caret(0));
    }

    public void LoadUntitledText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ReplaceEditorText(text);
        _session.SetUntitled(
            text,
            TextEncodingKind.Utf8,
            DocumentSession.GetLineEndingOrDefault(TextFileService.DetectLineEnding(text)));
    }

    public void NewDocument()
    {
        ReplaceEditorText(string.Empty);
        _session.SetUntitled(
            string.Empty,
            TextEncodingKind.Utf8,
            DocumentSession.GetDefaultLineEnding());
    }

    public Task<bool> SaveAsync(CancellationToken cancellationToken = default) =>
        SaveAsync(editorConfig: null, cancellationToken: cancellationToken);

    public async Task<bool> SaveAsync(
        EditorConfigSettings? editorConfig,
        CancellationToken cancellationToken = default)
    {
        if (_session.State.IsReadOnly) return false;
        var path = _session.State.FilePath;
        if (path is null)
        {
            return false;
        }

        var currentText = _editor.Text;
        var textToSave = EditorConfigTextNormalizer.NormalizeForSave(
            currentText,
            editorConfig,
            _session.State.LineEnding);
        await _files.WriteAsync(
            path,
            textToSave,
            _session.State.Encoding,
            _session.State.LineEnding,
            cancellationToken);
        ApplySavedText(textToSave);
        _session.MarkSaved(
            path,
            _editor.Text,
            _session.State.Encoding,
            _session.State.LineEnding);
        return true;
    }

    public Task SaveAsAsync(
        SaveFileDialogResult save,
        CancellationToken cancellationToken = default) =>
        SaveAsAsync(
            save,
            editorConfig: null,
            cancellationToken: cancellationToken);

    public async Task SaveAsAsync(
        SaveFileDialogResult save,
        EditorConfigSettings? editorConfig,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(save);
        var path = Path.GetFullPath(save.Path);
        if (_session.State.IsReadOnly || BundledLegalDocuments.IsReadOnlyPath(path))
        {
            throw new InvalidOperationException("The document is read-only.");
        }
        var lineEnding = DocumentSession.GetLineEndingOrDefault(save.LineEnding);
        var textToSave = EditorConfigTextNormalizer.NormalizeForSave(
            _editor.Text,
            editorConfig,
            lineEnding);
        await _files.WriteAsync(
            path,
            textToSave,
            save.Encoding,
            save.LineEnding,
            cancellationToken);
        ApplySavedText(textToSave);
        _session.MarkSaved(path, _editor.Text, save.Encoding, lineEnding);
    }

    public Task<bool> ReloadFromDiskAsync(CancellationToken cancellationToken = default) =>
        ReloadFromDiskAsync(encodingHint: null, cancellationToken: cancellationToken);

    public async Task<bool> ReloadFromDiskAsync(
        TextEncodingKind? encodingHint,
        CancellationToken cancellationToken = default)
    {
        var path = _session.State.FilePath;
        if (path is null)
        {
            return false;
        }

        var data = await _files.ReadAsync(path, encodingHint, cancellationToken);
        ReplaceEditorTextAsEdit(data.Text);
        _session.ApplyDiskReload(path, data);
        return true;
    }

    public Task<bool> ReloadFromTemporaryFileAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        ReloadFromTemporaryFileAsync(
            path,
            encodingHint: null,
            cancellationToken: cancellationToken);

    public async Task<bool> ReloadFromTemporaryFileAsync(
        string path,
        TextEncodingKind? encodingHint,
        CancellationToken cancellationToken = default)
    {
        if (_session.State.IsReadOnly) return false;
        var data = await _files.ReadAsync(path, encodingHint, cancellationToken);
        ReplaceEditorTextAsEdit(data.Text);
        _session.ApplyTemporaryReload(data, data.Text);
        return true;
    }

    public async Task<bool> ApplyExternalChangeAsync(
        TextFileData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var path = _session.State.FilePath;
        if (path is null || cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (_session.State.IsDirty
            && await _prompt.ResolveExternalChangeAsync() != ExternalChangeDecision.Reload)
        {
            return false;
        }

        ReplaceEditorTextAsEdit(data.Text);
        _session.ApplyDiskReload(path, data);
        return true;
    }

    private void ReplaceEditorText(string text)
    {
        _editor.SetText(text);
        _editor.SetSelection(TextSelection.Caret(0));
    }

    private void RestoreSelection(TextSelection selection, int textLength)
    {
        _editor.SetSelection(new TextSelection(
            Math.Min(selection.Anchor, textLength),
            Math.Min(selection.Active, textLength)));
    }

    private void ApplySavedText(string text) => ReplaceEditorTextAsEdit(text);

    /// <summary>
    /// Replaces the whole buffer through an ordinary edit, so every reload --
    /// the one a changed file triggers, an external tool's reloadFile, and the
    /// text a save normalized -- stays reachable through undo. A read-only
    /// document takes no edit, so its contents are reset instead.
    /// </summary>
    private void ReplaceEditorTextAsEdit(string text)
    {
        if (string.Equals(_editor.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        if (_session.State.IsReadOnly)
        {
            ReplaceEditorText(text);
            return;
        }

        var selection = _editor.Selection;
        _editor.Replace(
            TextRange.FromBounds(0, _editor.Text.Length),
            text);
        RestoreSelection(selection, text.Length);
    }
}
