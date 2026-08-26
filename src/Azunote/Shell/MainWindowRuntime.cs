using Azunyan.Core;

namespace Azunote;

internal sealed class MainWindowRuntime : IDisposable
{
    private readonly MainWindowViewAdapter _view;
    private readonly DocumentSession _session;
    private readonly WinUiUserPrompt _prompt;
    private readonly DispatcherQueueUiDispatcher _dispatcher;
    private readonly LanguageModeController _languageModes;
    private readonly DocumentWorkflow _documents;
    private readonly SettingsWorkflow _settings;
    private readonly EditorCommandController _editorCommands;
    private readonly FindReplaceController _findReplace;
    private readonly DocumentStatusPresenter _status;
    private readonly WinUiMessageDialog _messageDialog;
    private readonly WinUiExternalToolDialog _externalToolDialog;
    private bool _disposed;

    public MainWindowRuntime(MainWindowViewAdapter view)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _session = new DocumentSession();
        _prompt = new WinUiUserPrompt(() => _view.XamlRoot);
        _dispatcher = new DispatcherQueueUiDispatcher(_view.DispatcherQueue);
        var files = new TextFileStore();
        var documents = new DocumentController(_view, _session, files, _prompt);
        var settings = new SettingsController(SettingsFileService.GetDefaultDirectory());
        _languageModes = new LanguageModeController(
            _view,
            _view,
            () => _session.State.FilePath);
        _languageModes.Initialize();

        var externalTools = new ExternalToolController(
            _view,
            documents,
            files,
            _prompt,
            () => _languageModes.CurrentModeId);

        _documents = new DocumentWorkflow(
            _view,
            documents,
            externalTools,
            new NativeFileDialogService(_view.WindowHandle),
            new DefaultFileChangeMonitorFactory(),
            _dispatcher,
            _prompt,
            _languageModes.GetFileDialogFilters,
            () => _languageModes.CurrentModeId);
        _settings = new SettingsWorkflow(
            settings,
            _languageModes,
            _view,
            new DefaultFileChangeMonitorFactory(),
            _dispatcher,
            _prompt,
            new WinUiSettingsFolderOpener(),
            () => _session.State.FilePath,
            RunConfiguredExternalToolAsync,
            GetExternalToolMenuState);
        _editorCommands = new EditorCommandController(_view, _view);
        _status = new DocumentStatusPresenter(_view, _session, _view, _view);
        _findReplace = new FindReplaceController(
            _view,
            _view,
            RefreshDocumentView,
            ObserveTextChanged);
        _messageDialog = new WinUiMessageDialog(() => _view.XamlRoot);
        _externalToolDialog = new WinUiExternalToolDialog(() => _view.XamlRoot);

        _documents.Changed += Documents_Changed;
        _languageModes.Changed += LanguageModes_Changed;
        RefreshDocumentView();
    }

    public bool IsFindPanelVisible => _view.IsVisible;

    public bool IsFindBoxFocused => _view.IsFindBoxFocused;

    public bool IsDirty => _documents.IsDirty;

    public Task InitializeSettingsAsync() => _settings.InitializeAsync();

    public Task OpenStartupDocumentAsync(
        string path,
        int? line = null,
        int? column = null) =>
        _documents.OpenStartupDocumentAsync(path, line, column);

    public Task OpenStartupTextAsync(
        string text,
        int? line = null,
        int? column = null) =>
        _documents.OpenStartupTextAsync(text, line, column);

    public Task<ExternalToolResult> RunExternalToolAsync(
        ExternalToolDefinition definition,
        CancellationToken cancellationToken = default) =>
        _documents.RunExternalToolAsync(definition, cancellationToken);

    public Task OpenFileAsync() => _documents.OpenFileAsync();

    public Task NewDocumentAsync() => _documents.NewDocumentAsync();

    public Task<bool> SaveAsync() => _documents.SaveAsync();

    public Task<bool> SaveAsAsync() => _documents.SaveAsAsync();

    public Task<bool> TryConfirmCloseAsync() => _documents.TryConfirmCloseAsync();

    public Task OpenPreferencesAsync() => _settings.OpenPreferencesAsync();

    public Task ShowExternalToolDialogAsync() => RunExternalToolDialogAsync();

    public Task ShowAboutAsync() => ShowAboutDialogAsync();

    public Task ShowCommandLineHelpAsync() => _prompt.ShowErrorAsync(
        "Azunote command line",
        AzunoteCommandLine.Usage);

    public Task ShowStartupErrorAsync(string title, string message) =>
        _prompt.ShowErrorAsync(title, message);

    public void ShowUnhandledError(Exception exception, string logPath)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);

        var detail = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;
        var message = $"{detail}\n\nDetails were written to:\n{logPath}";
        _dispatcher.TryEnqueue(() => _ = _prompt.ShowErrorAsync(
            "Azunote encountered an unexpected error",
            message));
    }

    public void Undo() => _editorCommands.Undo();

    public void Redo() => _editorCommands.Redo();

    public void Cut() => _editorCommands.Cut();

    public void Copy() => _editorCommands.Copy();

    public void Paste() => _editorCommands.Paste();

    public void SelectAll() => _editorCommands.SelectAll();

    public void ToggleWordWrap() => _editorCommands.ToggleWordWrap();

    public void ToggleStatusBar() => _editorCommands.ToggleStatusBar();

    public void ShowCompletion() => _editorCommands.ShowCompletion();

    public void ShowFindPanel(bool replace) => _findReplace.Show(replace);

    public void CloseFindPanel() => _findReplace.Close();

    public void OnFindTextChanged() => _findReplace.OnFindTextChanged();

    public void FindNext() => _findReplace.FindNext();

    public void ReplaceCurrent() => _findReplace.ReplaceCurrent();

    public void ReplaceAll() => _findReplace.ReplaceAll();

    public void ObserveTextChanged()
    {
        _documents.ObserveTextChanged();
        RefreshDocumentView();
    }

    public void RefreshStatus() => _status.Refresh();

    public void RefreshExternalToolsMenu() => _settings.RefreshExternalToolsMenu();

    public void OpenDroppedFile(string path) => _documents.OpenDroppedFile(path);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _languageModes.Changed -= LanguageModes_Changed;
        _documents.Changed -= Documents_Changed;
        _documents.Dispose();
        _settings.Dispose();
        _view.DisposeEditor();
    }

    private async Task RunExternalToolDialogAsync()
    {
        var definition = await _externalToolDialog.ShowAsync();
        if (definition is not null)
        {
            await RunExternalToolAsync(definition);
        }
    }

    private async Task RunConfiguredExternalToolAsync(ExternalToolSettings tool)
    {
        try
        {
            var state = GetExternalToolMenuState(tool);
            if (!state.IsEnabled)
            {
                await _prompt.ShowErrorAsync(
                    "External tool is unavailable",
                    state.DisabledReason ?? "The external tool cannot run in the current context.");
                RefreshExternalToolsMenu();
                return;
            }

            await RunExternalToolAsync(tool.ToDefinition());
        }
        catch (OperationCanceledException)
        {
            // The configured tool was canceled by the caller.
        }
        catch (Exception exception)
        {
            ErrorReporter.LogException($"External tool failed: {tool.Name}", exception);
        }
    }

    private async Task ShowAboutDialogAsync()
    {
        try
        {
            await _messageDialog.ShowAsync(
                "About Azunote",
                "Azunote\nA small WinUI 3 single-document text editor.");
        }
        catch (Exception exception)
        {
            ErrorReporter.LogException("About dialog failure", exception);
        }
    }

    private void Documents_Changed(
        object? sender,
        DocumentWorkflowChangedEventArgs args)
    {
        if (args.Opened && _documents.CurrentFilePath is { } path)
        {
            _languageModes.DocumentOpened(path);
        }

        _status.Refresh(args.LineEnding);
        _status.RefreshTitle();
        RefreshExternalToolsMenu();
    }

    private void LanguageModes_Changed(object? sender, EventArgs args) =>
        RefreshExternalToolsMenu();

    private ExternalToolMenuState GetExternalToolMenuState(ExternalToolSettings tool)
    {
        var snapshot = _view.Snapshot;
        var lineColumn = snapshot.Lines.GetLineColumn(_view.CaretPosition);
        var selectionStart = snapshot.Lines.GetLineColumn(_view.Selection.Start);
        var selectionEnd = snapshot.Lines.GetLineColumn(_view.Selection.End);
        var state = _session.State;
        var context = new ExternalToolContext(
            state.FilePath,
            state.FilePath,
            _view.Text,
            _view.SelectedText,
            lineColumn.Line + 1,
            lineColumn.Column + 1,
            _languageModes.CurrentModeId,
            tool.DefinitionDirectory,
            state.Encoding,
            state.LineEnding,
            state.IsDirty,
            selectionStart,
            selectionEnd);
        return ExternalToolAvailability.Evaluate(tool, context);
    }

    private void RefreshDocumentView()
    {
        _status.Refresh();
        _status.RefreshTitle();
        RefreshExternalToolsMenu();
    }
}
