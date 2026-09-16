using Azunyan.Core;

namespace Azunote;

/// <summary>
/// Applies external-tool results to the current editor buffer or opens them in
/// a new window. Process execution and output interpretation remain in
/// ExternalTools.cs.
/// </summary>
public sealed class ExternalToolController
{
    private readonly IEditorBuffer _editor;
    private readonly DocumentController _documents;
    private readonly ITextFileStore _files;
    private readonly IUserPrompt _prompt;
    private readonly Func<string, Task> _openTextInNewWindow;
    private readonly Func<string> _languageModeId;
    private readonly Func<IReadOnlyList<string>> _languageExtensions;
    private readonly PowerShellWarmPool? _warmPool;

    public ExternalToolController(
        IEditorBuffer editor,
        DocumentController documents,
        ITextFileStore files,
        IUserPrompt prompt,
        Func<string, Task> openTextInNewWindow,
        Func<string>? languageModeId = null,
        Func<IReadOnlyList<string>>? languageExtensions = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _openTextInNewWindow = openTextInNewWindow
            ?? throw new ArgumentNullException(nameof(openTextInNewWindow));
        _languageModeId = languageModeId ?? (() => string.Empty);
        _languageExtensions = languageExtensions ?? (() => []);
    }

    internal ExternalToolController(
        IEditorBuffer editor,
        DocumentController documents,
        ITextFileStore files,
        IUserPrompt prompt,
        Func<string, Task> openTextInNewWindow,
        Func<string>? languageModeId,
        PowerShellWarmPool? warmPool,
        Func<IReadOnlyList<string>>? languageExtensions = null)
        : this(editor, documents, files, prompt, openTextInNewWindow, languageModeId, languageExtensions) =>
        _warmPool = warmPool;

    public async Task<ExternalToolResult> RunAsync(
        ExternalToolDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var editorSnapshot = EditorBufferSnapshot.Capture(_editor);
        var selection = editorSnapshot.Selection;
        var filePath = _documents.Session.State.FilePath;
        var temporaryFilePath = _documents.Session.State.IsDirty || filePath is null
            ? CreateTemporaryFilePath(filePath)
            : null;
        try
        {
            if (temporaryFilePath is not null)
            {
                await _files.WriteAsync(
                    temporaryFilePath,
                    editorSnapshot.Text,
                    _documents.Session.State.Encoding,
                    _documents.Session.State.LineEnding,
                    cancellationToken);
            }

            var context = new ExternalToolContext(
                filePath,
                temporaryFilePath ?? filePath,
                editorSnapshot.Text,
                editorSnapshot.SelectedText,
                editorSnapshot.Caret.Line + 1,
                editorSnapshot.Caret.Column + 1,
                _languageModeId(),
                definition.DefinitionDirectory,
                _documents.Session.State.Encoding,
                _documents.Session.State.LineEnding,
                _documents.Session.State.IsDirty,
                editorSnapshot.SelectionStart,
                editorSnapshot.SelectionEnd,
                languageExtensions: _languageExtensions());
            var result = await ExternalToolRunner.RunAsync(
                definition,
                context,
                _warmPool,
                cancellationToken);
            var output = ExternalToolOutputInterpreter.Interpret(definition, result);
            foreach (var action in output.Actions)
            {
                await ApplyOutputActionAsync(
                    action,
                    selection,
                    temporaryFilePath,
                    filePath,
                    cancellationToken);
            }

            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await _prompt.ShowErrorAsync("Could not run external tool", exception.Message);
            throw;
        }
        finally
        {
            DeleteTemporaryFile(temporaryFilePath);
        }
    }

    private async Task ApplyOutputActionAsync(
        ExternalToolOutputAction action,
        TextSelection selection,
        string? temporaryFilePath,
        string? filePath,
        CancellationToken cancellationToken)
    {
        switch (action.Mode)
        {
            case ExternalToolOutputMode.Ignore:
                return;
            case ExternalToolOutputMode.ReplaceDocument:
                _editor.Replace(new TextRange(0, _editor.Text.Length), action.Text);
                _documents.Session.ObserveText(_editor.Text);
                return;
            case ExternalToolOutputMode.ReplaceSelection:
                if (selection.End > _editor.Text.Length)
                {
                    await _prompt.ShowErrorAsync(
                        "Could not apply external tool output",
                        "The selection changed while the tool was running.");
                }
                else
                {
                    _editor.Replace(selection.Range, action.Text);
                    _editor.SetSelection(SelectReplacement(selection, action.Text.Length));
                    _documents.Session.ObserveText(_editor.Text);
                }

                return;
            case ExternalToolOutputMode.NewDocument:
                await _openTextInNewWindow(action.Text);
                return;
            case ExternalToolOutputMode.ReloadFile:
                await ReloadOutputAsync(temporaryFilePath, filePath, cancellationToken);
                return;
            case ExternalToolOutputMode.ShowCompletion:
                if (_editor is not IEditorView editorView)
                {
                    throw new InvalidOperationException(
                        "The current editor does not support completion windows.");
                }

                editorView.ShowCompletion(
                    new CompletionResult(
                        new TextRange(_editor.CaretPosition, 0),
                        ExternalToolOutputInterpreter.CreateCompletionItems(action.Text)));
                return;
            default:
                throw new InvalidOperationException($"Unsupported external-tool output mode: {action.Mode}.");
        }
    }

    /// <summary>
    /// Keeps the replaced text selected, preserving the direction the original
    /// selection was made in.
    /// </summary>
    private static TextSelection SelectReplacement(TextSelection selection, int length)
    {
        var start = selection.Start;
        var end = start + length;
        return selection.IsReversed
            ? new TextSelection(end, start)
            : new TextSelection(start, end);
    }

    private async Task ReloadOutputAsync(
        string? temporaryFilePath,
        string? filePath,
        CancellationToken cancellationToken)
    {
        if (temporaryFilePath is not null)
        {
            await _documents.ReloadFromTemporaryFileAsync(
                temporaryFilePath,
                _documents.Session.State.Encoding,
                cancellationToken);
            return;
        }

        if (filePath is null)
        {
            await _prompt.ShowErrorAsync(
                "Could not reload file",
                "The current document is not backed by a file.");
            return;
        }

        if (_documents.Session.State.IsDirty)
        {
            var changedDocument = await _files.ReadAsync(
                filePath,
                _documents.Session.State.Encoding,
                cancellationToken);
            await _documents.ApplyExternalChangeAsync(changedDocument, cancellationToken);
        }
        else
        {
            await _documents.ReloadFromDiskAsync(
                _documents.Session.State.Encoding,
                cancellationToken);
        }
    }

    private static string CreateTemporaryFilePath(string? filePath)
    {
        var extension = filePath is null ? ".txt" : Path.GetExtension(filePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".txt";
        }

        return Path.Combine(
            Path.GetTempPath(),
            $"azunote-external-{Guid.NewGuid():N}{extension}");
    }

    private static void DeleteTemporaryFile(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
