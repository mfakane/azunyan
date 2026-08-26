using Azunyan.Core;

namespace Azunote;

/// <summary>
/// Applies external-tool results to the current editor buffer. Process
/// execution and output interpretation remain in ExternalTools.cs.
/// </summary>
public sealed class ExternalToolController
{
    private readonly IEditorBuffer _editor;
    private readonly DocumentController _documents;
    private readonly ITextFileStore _files;
    private readonly IUserPrompt _prompt;
    private readonly Func<string> _languageModeId;

    public ExternalToolController(
        IEditorBuffer editor,
        DocumentController documents,
        ITextFileStore files,
        IUserPrompt prompt,
        Func<string>? languageModeId = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _languageModeId = languageModeId ?? (() => string.Empty);
    }

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
                editorSnapshot.SelectionEnd);
            var result = await ExternalToolRunner.RunAsync(
                definition,
                context,
                cancellationToken);
            var output = ExternalToolOutputInterpreter.Interpret(definition, result);
            if (!output.IsSuccess)
            {
                await _prompt.ShowErrorAsync("External tool failed", output.Error!);
                return result;
            }

            if (output.ReloadFile)
            {
                await ReloadOutputAsync(temporaryFilePath, filePath, cancellationToken);
                return result;
            }

            if (output.ReplacementText is not null)
            {
                switch (definition.OutputMode)
                {
                    case ExternalToolOutputMode.ReplaceDocument:
                        _editor.Replace(new TextRange(0, _editor.Text.Length), output.ReplacementText);
                        _documents.Session.ObserveText(_editor.Text);
                        break;
                    case ExternalToolOutputMode.ReplaceSelection:
                        if (selection.End > _editor.Text.Length)
                        {
                            await _prompt.ShowErrorAsync(
                                "Could not apply external tool output",
                                "The selection changed while the tool was running.");
                        }
                        else
                        {
                            _editor.Replace(selection.Range, output.ReplacementText);
                            _documents.Session.ObserveText(_editor.Text);
                        }

                        break;
                    case ExternalToolOutputMode.NewDocument:
                        _documents.LoadUntitledText(output.ReplacementText);
                        break;
                }
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

    private async Task ReloadOutputAsync(
        string? temporaryFilePath,
        string? filePath,
        CancellationToken cancellationToken)
    {
        if (temporaryFilePath is not null)
        {
            await _documents.ReloadFromTemporaryFileAsync(temporaryFilePath, cancellationToken);
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
            var changedDocument = await _files.ReadAsync(filePath, cancellationToken);
            await _documents.ApplyExternalChangeAsync(changedDocument, cancellationToken);
        }
        else
        {
            await _documents.ReloadFromDiskAsync(cancellationToken);
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
