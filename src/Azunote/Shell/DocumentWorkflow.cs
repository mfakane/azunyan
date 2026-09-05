using Azunyan.Core;

namespace Azunote;

internal sealed class DocumentWorkflowChangedEventArgs : EventArgs
{
    public DocumentWorkflowChangedEventArgs(
        LineEndingKind? lineEnding,
        bool opened)
    {
        LineEnding = lineEnding;
        Opened = opened;
    }

    public LineEndingKind? LineEnding { get; }

    public bool Opened { get; }
}

internal sealed class DocumentWorkflow : IDisposable
{
    private readonly IEditorView _editor;
    private readonly DocumentController _documents;
    private readonly ExternalToolController _externalTools;
    private readonly IFileDialogService _fileDialogs;
    private readonly IFileChangeMonitorFactory _monitorFactory;
    private readonly IUiDispatcher _dispatcher;
    private readonly IUserPrompt _prompt;
    private readonly Func<IReadOnlyList<FileDialogFilter>> _fileDialogFilters;
    private readonly Func<string> _languageModeId;
    private readonly Func<Task> _createNewWindow;
    private readonly Func<string, Task> _openFileInNewWindow;
    private readonly IEditorConfigResolver _editorConfigResolver;
    private EditorConfigSettings _editorConfig = EditorConfigSettings.Empty;
    private IFileChangeMonitor? _fileMonitor;
    private string? _monitoredPath;
    private bool _isApplying;
    private bool _externalChangeDialogOpen;
    private bool _allowClose;
    private bool _disposed;

    public DocumentWorkflow(
        IEditorView editor,
        DocumentController documents,
        ExternalToolController externalTools,
        IFileDialogService fileDialogs,
        IFileChangeMonitorFactory monitorFactory,
        IUiDispatcher dispatcher,
        IUserPrompt prompt,
        Func<IReadOnlyList<FileDialogFilter>> fileDialogFilters,
        Func<string> languageModeId,
        Func<Task> createNewWindow,
        Func<string, Task> openFileInNewWindow,
        IEditorConfigResolver? editorConfigResolver = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _externalTools = externalTools ?? throw new ArgumentNullException(nameof(externalTools));
        _fileDialogs = fileDialogs ?? throw new ArgumentNullException(nameof(fileDialogs));
        _monitorFactory = monitorFactory ?? throw new ArgumentNullException(nameof(monitorFactory));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _fileDialogFilters = fileDialogFilters ?? throw new ArgumentNullException(nameof(fileDialogFilters));
        _languageModeId = languageModeId ?? throw new ArgumentNullException(nameof(languageModeId));
        _createNewWindow = createNewWindow ?? throw new ArgumentNullException(nameof(createNewWindow));
        _openFileInNewWindow = openFileInNewWindow ?? throw new ArgumentNullException(nameof(openFileInNewWindow));
        _editorConfigResolver = editorConfigResolver ?? new EditorConfigResolver();
    }

    public event EventHandler<DocumentWorkflowChangedEventArgs>? Changed;

    public DocumentSessionState State => _documents.Session.State;

    public bool IsDirty => State.IsDirty;

    public string? CurrentFilePath => State.FilePath;

    public string CurrentDocumentName => CurrentFilePath is { } path
        ? Path.GetFileName(path)
        : "Untitled";

    public TextEncodingKind CurrentEncoding => State.Encoding;

    public LineEndingKind CurrentLineEnding => State.LineEnding;

    public bool IsApplying => _isApplying;

    public bool IsFileWatcherActive => _fileMonitor is not null;

    public void EnsureFileWatcher() => StartFileWatcher();

    public void StopFileWatcherIfPathChanged()
    {
        if (_monitoredPath is not null
            && (CurrentFilePath is null
                || !string.Equals(
                    _monitoredPath,
                    Path.GetFullPath(CurrentFilePath),
                    StringComparison.OrdinalIgnoreCase)))
        {
            StopFileWatcher();
        }
    }

    public async Task OpenFileAsync()
    {
        try
        {
            var path = _fileDialogs.ShowOpen(_fileDialogFilters());
            if (path is null)
            {
                return;
            }

            if (CurrentFilePath is null && !IsDirty)
            {
                await LoadDocumentAsync(path);
            }
            else
            {
                await _openFileInNewWindow(path);
            }
        }
        catch (Exception exception)
        {
            await _prompt.ShowErrorAsync("Could not open the file", exception.Message);
        }
    }

    public async Task OpenRecentFileAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            if (CurrentFilePath is null && !IsDirty)
            {
                await LoadDocumentAsync(path);
            }
            else
            {
                await _openFileInNewWindow(path);
            }
        }
        catch (Exception exception)
        {
            await _prompt.ShowErrorAsync("Could not open the recent file", exception.Message);
        }
    }

    public async Task OpenStartupDocumentAsync(
        string path,
        int? line = null,
        int? column = null)
    {
        try
        {
            await LoadDocumentAsync(path);
            _editor.SetStartupPosition(line, column);
        }
        catch (Exception exception)
        {
            await _prompt.ShowErrorAsync("Could not open the file", exception.Message);
        }
    }

    public Task OpenStartupTextAsync(
        string text,
        int? line = null,
        int? column = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        StopFileWatcher();
        ApplyDocument(() => _documents.LoadUntitledText(text));
        ApplyEditorConfig(EditorConfigSettings.Empty);
        NotifyChanged(lineEnding: null, opened: false);
        _editor.SetStartupPosition(line, column);
        _editor.Focus();
        return Task.CompletedTask;
    }

    public async Task NewDocumentAsync()
    {
        await _createNewWindow();
    }

    public async Task<bool> SaveAsync()
    {
        if (CurrentFilePath is null)
        {
            return await SaveAsAsync();
        }

        try
        {
            await _documents.SaveAsync(editorConfig: _editorConfig);
            NotifyChanged(lineEnding: null, opened: false);
            StartFileWatcher();
            return true;
        }
        catch (Exception exception)
        {
            await _prompt.ShowErrorAsync("Could not save the file", exception.Message);
            return false;
        }
    }

    public async Task<bool> SaveAsAsync()
    {
        try
        {
            var save = _fileDialogs.ShowSave(
                CurrentFilePath is null ? "Untitled.txt" : Path.GetFileName(CurrentFilePath),
                CurrentEncoding,
                CurrentLineEnding,
                _fileDialogFilters(),
                _languageModeId());
            if (save is null)
            {
                return false;
            }

            var targetConfig = await _editorConfigResolver.ResolveAsync(save.Path);
            await _documents.SaveAsAsync(save, editorConfig: targetConfig);
            ApplyEditorConfig(targetConfig.WithExplicitFileFormat(
                save.Encoding,
                DocumentSession.GetLineEndingOrDefault(save.LineEnding)));
            NotifyChanged(lineEnding: null, opened: false);
            StartFileWatcher();
            return true;
        }
        catch (Exception exception)
        {
            await _prompt.ShowErrorAsync("Could not save the file", exception.Message);
            return false;
        }
    }

    public async Task ReloadFromDiskAsync()
    {
        if (CurrentFilePath is not { } currentPath)
        {
            return;
        }

        StopFileWatcher();
        var editorConfig = await _editorConfigResolver.ResolveAsync(currentPath);
        await ApplyDocumentAsync(() => _documents.ReloadFromDiskAsync(
            encodingHint: editorConfig.Encoding));
        ApplyEditorConfig(editorConfig);
        NotifyChanged(
            editorConfig.LineEnding ?? TextFileService.DetectLineEnding(_editor.Text),
            opened: false);
        StartFileWatcher();
    }

    public async Task ReloadFromTemporaryFileAsync(string path)
    {
        if (!File.Exists(path))
        {
            await _prompt.ShowErrorAsync(
                "Could not reload file",
                "The external tool did not leave the temporary file available.");
            return;
        }

        var editorConfig = await ResolveCurrentEditorConfigAsync();
        await ApplyDocumentAsync(() => _documents.ReloadFromTemporaryFileAsync(
            path,
            encodingHint: editorConfig.Encoding));
        ApplyEditorConfig(editorConfig);
        NotifyChanged(lineEnding: null, opened: false);
    }

    public async Task<ExternalToolResult> RunExternalToolAsync(
        ExternalToolDefinition definition,
        CancellationToken cancellationToken = default)
    {
        StopFileWatcher();
        try
        {
            return await _externalTools.RunAsync(definition, cancellationToken);
        }
        finally
        {
            if (CurrentFilePath is not null)
            {
                StartFileWatcher();
            }
            else
            {
                StopFileWatcher();
            }

            if (CurrentFilePath is null)
            {
                ApplyEditorConfig(EditorConfigSettings.Empty);
            }

            NotifyChanged(lineEnding: null, opened: false);
        }
    }

    public async Task<bool> ConfirmPendingChangesAsync() =>
        await _documents.ConfirmPendingChangesAsync(SaveAsync);

    public async Task<bool> TryConfirmCloseAsync()
    {
        if (_allowClose || !IsDirty)
        {
            return true;
        }

        if (!await ConfirmPendingChangesAsync())
        {
            return false;
        }

        _allowClose = true;
        return true;
    }

    public void ObserveTextChanged()
    {
        if (_isApplying)
        {
            return;
        }

        _documents.Session.ObserveText(_editor.Text);
    }

    public void OpenDroppedFile(string path)
    {
        _ = OpenDroppedFileAsync(path);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopFileWatcher();
    }

    private async Task OpenDroppedFileAsync(string path)
    {
        if (!await ConfirmPendingChangesAsync())
        {
            return;
        }

        try
        {
            await LoadDocumentAsync(path);
        }
        catch (Exception exception)
        {
            await _prompt.ShowErrorAsync("Could not open the dropped file", exception.Message);
        }
    }

    private async Task LoadDocumentAsync(string path)
    {
        StopFileWatcher();
        var editorConfig = await _editorConfigResolver.ResolveAsync(path);
        await ApplyDocumentAsync(() => _documents.OpenAsync(
            path,
            encodingHint: editorConfig.Encoding));
        ApplyEditorConfig(editorConfig);
        NotifyChanged(
            editorConfig.LineEnding ?? TextFileService.DetectLineEnding(_editor.Text),
            opened: true);
        _editor.Focus();
        StartFileWatcher();
    }

    private async Task<EditorConfigSettings> ResolveCurrentEditorConfigAsync()
    {
        return CurrentFilePath is { } path
            ? await _editorConfigResolver.ResolveAsync(path)
            : EditorConfigSettings.Empty;
    }

    private void ApplyEditorConfig(EditorConfigSettings settings)
    {
        _editorConfig = settings;
        _documents.Session.ApplyEditorConfig(settings);
        _editor.ApplyEditorConfig(settings);
    }

    private void ApplyDocument(Action action)
    {
        _isApplying = true;
        try
        {
            action();
        }
        finally
        {
            _isApplying = false;
        }
    }

    private async Task ApplyDocumentAsync(Func<Task> action)
    {
        _isApplying = true;
        try
        {
            await action();
        }
        finally
        {
            _isApplying = false;
        }
    }

    private void NotifyChanged(LineEndingKind? lineEnding, bool opened) =>
        Changed?.Invoke(this, new DocumentWorkflowChangedEventArgs(lineEnding, opened));

    private void StartFileWatcher()
    {
        StopFileWatcher();
        if (CurrentFilePath is not { } path || _disposed)
        {
            return;
        }

        _fileMonitor = _monitorFactory.Create(path, includeSubdirectories: false);
        _monitoredPath = Path.GetFullPath(path);
        _fileMonitor.Changed += FileMonitor_Changed;
    }

    private void StopFileWatcher()
    {
        if (_fileMonitor is null)
        {
            return;
        }

        _fileMonitor.Changed -= FileMonitor_Changed;
        _fileMonitor.Dispose();
        _fileMonitor = null;
        _monitoredPath = null;
    }

    private void FileMonitor_Changed(
        object? sender,
        FileChangeDetectedEventArgs args)
    {
        if (ReferenceEquals(sender, _fileMonitor))
        {
            _dispatcher.TryEnqueue(() => _ = HandleFileChangedAsync(args.MonitoredPath));
        }
    }

    private async Task HandleFileChangedAsync(string expectedPath)
    {
        try
        {
            if (_disposed
                || CurrentFilePath is not { } path
                || !string.Equals(
                    Path.GetFullPath(expectedPath),
                    Path.GetFullPath(path),
                    StringComparison.OrdinalIgnoreCase)
                || !File.Exists(path))
            {
                return;
            }

            var editorConfig = await _editorConfigResolver.ResolveAsync(path);
            var document = await _documents.ReadAsync(
                path,
                encodingHint: editorConfig.Encoding);
            if (_documents.Session.IsSameAsSaved(document.Text))
            {
                return;
            }

            await ApplyExternalFileChangeAsync(document);
        }
        catch (OperationCanceledException)
        {
            // A burst of filesystem notifications was superseded by a newer one.
        }
        catch (Exception exception)
        {
            await _prompt.ShowErrorAsync("Could not reload the changed file", exception.Message);
        }
    }

    private async Task ApplyExternalFileChangeAsync(
        TextFileData document,
        CancellationToken cancellationToken = default)
    {
        if (CurrentFilePath is null
            || _externalChangeDialogOpen
            || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        _externalChangeDialogOpen = true;
        try
        {
            StopFileWatcher();
            await ApplyDocumentAsync(() => _documents.ApplyExternalChangeAsync(document, cancellationToken));
        }
        finally
        {
            _externalChangeDialogOpen = false;
        }

        ApplyEditorConfig(await ResolveCurrentEditorConfigAsync());
        NotifyChanged(
            _editorConfig.LineEnding ?? TextFileService.DetectLineEnding(_editor.Text),
            opened: false);
        StartFileWatcher();
    }
}
