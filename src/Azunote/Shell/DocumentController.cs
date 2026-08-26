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

    public async Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        var data = await _files.ReadAsync(path, cancellationToken);
        ReplaceEditorText(data.Text);
        _session.Load(path, data);
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

    public async Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        var path = _session.State.FilePath;
        if (path is null)
        {
            return false;
        }

        await _files.WriteAsync(
            path,
            _editor.Text,
            _session.State.Encoding,
            _session.State.LineEnding,
            cancellationToken);
        _session.MarkSaved(
            path,
            _editor.Text,
            _session.State.Encoding,
            _session.State.LineEnding);
        return true;
    }

    public async Task SaveAsAsync(
        SaveFileDialogResult save,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(save);
        var path = Path.GetFullPath(save.Path);
        await _files.WriteAsync(
            path,
            _editor.Text,
            save.Encoding,
            save.LineEnding,
            cancellationToken);
        _session.MarkSaved(path, _editor.Text, save.Encoding, save.LineEnding);
    }

    public async Task<bool> ReloadFromDiskAsync(CancellationToken cancellationToken = default)
    {
        var path = _session.State.FilePath;
        if (path is null)
        {
            return false;
        }

        var data = await _files.ReadAsync(path, cancellationToken);
        var selection = _editor.Selection;
        ReplaceEditorText(data.Text);
        _session.ApplyDiskReload(path, data);
        RestoreSelection(selection, data.Text.Length);
        return true;
    }

    public async Task<bool> ReloadFromTemporaryFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var data = await _files.ReadAsync(path, cancellationToken);
        var selection = _editor.Selection;
        ReplaceEditorText(data.Text);
        _session.ApplyTemporaryReload(data, data.Text);
        RestoreSelection(selection, data.Text.Length);
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

        var selection = _editor.Selection;
        ReplaceEditorText(data.Text);
        _session.ApplyDiskReload(path, data);
        RestoreSelection(selection, data.Text.Length);
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
}
