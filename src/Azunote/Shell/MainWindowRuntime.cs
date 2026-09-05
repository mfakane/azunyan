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
    private readonly GoToLineController _goToLine;
    private readonly DocumentStatusPresenter _status;
    private readonly IFilePathActions _filePathActions;
    private readonly WinUiMessageDialog _messageDialog;
    private readonly WinUiExternalToolDialog _externalToolDialog;
    private readonly Action _refreshWindowMenus;
    private readonly Action<string> _recordRecentFile;
    private readonly Action<bool> _recordWordWrap;
    private readonly Action<bool> _recordStatusBarVisible;
    private bool _disposed;

    public MainWindowRuntime(
        MainWindowViewAdapter view,
        Func<Task> createNewWindow,
        Func<string, Task> openFileInNewWindow,
        Action refreshWindowMenus,
        IFilePathActions filePathActions,
        Action<string> recordRecentFile,
        Action<bool> recordWordWrap,
        Action<bool> recordStatusBarVisible,
        DocumentSession? session = null,
        Document? document = null)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _refreshWindowMenus = refreshWindowMenus ?? throw new ArgumentNullException(nameof(refreshWindowMenus));
        _filePathActions = filePathActions ?? throw new ArgumentNullException(nameof(filePathActions));
        _recordRecentFile = recordRecentFile ?? throw new ArgumentNullException(nameof(recordRecentFile));
        _recordWordWrap = recordWordWrap ?? throw new ArgumentNullException(nameof(recordWordWrap));
        _recordStatusBarVisible = recordStatusBarVisible ?? throw new ArgumentNullException(nameof(recordStatusBarVisible));
        _session = session ?? new DocumentSession();
        if (document is not null)
        {
            _view.SetDocument(document);
        }
        _prompt = new WinUiUserPrompt(() => _view.XamlRoot);
        _dispatcher = new DispatcherQueueUiDispatcher(_view.DispatcherQueue);
        var files = new TextFileStore();
        var documents = new DocumentController(_view, _session, files, _prompt);
        var settings = new SettingsController(SettingsFileService.GetDefaultDirectory());
        _languageModes = new LanguageModeController(
            _view,
            _view,
            () => _session.State.FilePath,
            OpenDefinitionAsync,
            ShowFileInExplorerAsync);
        _languageModes.Initialize();
        if (_session.State.FilePath is { } existingPath)
        {
            _languageModes.DocumentOpened(existingPath);
        }

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
            () => _languageModes.CurrentModeId,
            createNewWindow,
            openFileInNewWindow);
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
            GetExternalToolMenuState,
            OpenDefinitionAsync,
            ShowFileInExplorerAsync,
            ApplySettings);
        _editorCommands = new EditorCommandController(_view, _view);
        _view.SetTabDisplaySizeLabel(_view.TabDisplaySize);
        _view.SetIndentSizeLabel(_view.IndentSize);
        _view.SetIndentationInputModeLabel(_view.IndentationInputMode);
        _status = new DocumentStatusPresenter(
            _view,
            _session,
            _view,
            _view,
            () => _languageModes.CurrentModeDisplayName);
        _findReplace = new FindReplaceController(
            _view,
            _view,
            RefreshDocumentView,
            ObserveTextChanged);
        _goToLine = new GoToLineController(
            _view,
            new WinUiGoToLineDialog(() => _view.XamlRoot));
        _messageDialog = new WinUiMessageDialog(() => _view.XamlRoot);
        _externalToolDialog = new WinUiExternalToolDialog(() => _view.XamlRoot);

        _documents.Changed += Documents_Changed;
        _languageModes.Changed += LanguageModes_Changed;
        _session.StateChanged += Session_StateChanged;
        RefreshDocumentView();
    }

    public bool IsFindPanelVisible => _view.IsVisible;

    public bool IsFindBoxFocused => _view.IsFindBoxFocused;

    public bool IsDirty => _documents.IsDirty;

    internal DocumentSession Session => _session;

    internal Document CreateDocumentView() => _view.Document.CreateView();

    internal void EnsureFileWatcher() => _documents.EnsureFileWatcher();

    internal bool IsFileWatcherActive => _documents.IsFileWatcherActive;

    internal string DocumentName => _documents.CurrentDocumentName;

    public Task InitializeSettingsAsync() => _settings.InitializeAsync();

    private void ApplySettings(AzunoteSettings settings)
    {
        _view.SetDiagnosticLogging(settings.Debug.Logging);
        _view.SetFontFamily(settings.FontFamily);
        _view.SetFontSize(settings.FontSize);
    }

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

    public Task OpenRecentFileAsync(string path) => _documents.OpenRecentFileAsync(path);

    public Task OpenDefinitionAsync(string path) => OpenRecentFileAsync(path);

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

    public void MoveToMatchingBracket() => _editorCommands.MoveToMatchingBracket();

    public void ToggleWordWrap()
    {
        _recordWordWrap(_editorCommands.ToggleWordWrap());
    }

    public void ApplyViewState(AzunoteState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _editorCommands.SetWordWrap(state.WordWrap);
        _editorCommands.SetStatusBarVisible(state.StatusBarVisible);
    }

    public void SetTabDisplaySize(int size)
    {
        _editorCommands.SetTabDisplaySize(size);
        _status.Refresh();
    }

    public void SetIndentSize(int? size)
    {
        _editorCommands.SetIndentSize(size);
        _status.Refresh();
    }

    public void SetIndentationInputMode(IndentationInputMode mode)
    {
        _editorCommands.SetIndentationInputMode(mode);
        _status.Refresh();
    }

    public void ShowIndentationSizeMenu() => _view.ShowIndentationSizeMenu();

    public void ShowFilePathMenu() => _view.ShowFilePathMenu();

    public void CopyFilePath() =>
        _filePathActions.CopyFilePath(_session.State.FilePath ?? "Untitled");

    public async Task ShowFileInExplorerAsync()
    {
        if (_session.State.FilePath is not { } path)
        {
            return;
        }

        await ShowFileInExplorerAsync(path);
    }

    public async Task ShowFileInExplorerAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (Path.GetDirectoryName(fullPath) is not { } directory)
            {
                return;
            }

            var explorer = _settings.Current.Explorer ?? new ShellCommandSettings();
            var context = CreateExternalToolContext(filePathOverride: fullPath);
            var invocation = ResolveShellCommand(
                explorer,
                "explorer",
                context,
                directory);
            await _filePathActions.OpenExplorerAsync(
                invocation.Command,
                invocation.Arguments,
                invocation.WorkingDirectory);
        }
        catch (Exception exception)
        {
            await _prompt.ShowErrorAsync("Could not show file in Explorer", exception.Message);
        }
    }

    public async Task OpenFolderInTerminalAsync()
    {
        if (_session.State.FilePath is not { } path
            || Path.GetDirectoryName(path) is not { } directory)
        {
            return;
        }

        try
        {
            var terminal = _settings.Current.Terminal ?? new ShellCommandSettings();
            var context = CreateExternalToolContext();
            var invocation = ResolveShellCommand(terminal, "terminal", context, directory);
            await _filePathActions.OpenTerminalAsync(
                invocation.Command,
                invocation.Arguments,
                invocation.WorkingDirectory);
        }
        catch (Exception exception)
        {
            await _prompt.ShowErrorAsync("Could not open folder in Terminal", exception.Message);
        }
    }

    public void ToggleStatusBar()
    {
        _recordStatusBarVisible(_editorCommands.ToggleStatusBar());
    }

    public void ToggleAlwaysOnTop() => _view.ToggleAlwaysOnTop();

    public void ShowCompletion() => _editorCommands.ShowCompletion();

    public Task ShowGoToLineAsync() => _goToLine.ShowAsync();

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

    public void RenderRecentFiles(
        IReadOnlyList<string> paths,
        Action<string> removeRecentFile) =>
        _view.RenderRecentFiles(
            paths,
            OpenRecentFileAsync,
            _filePathActions.CopyFilePath,
            ShowFileInExplorerAsync,
            removeRecentFile);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.StateChanged -= Session_StateChanged;
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
            _recordRecentFile(path);
            _languageModes.DocumentOpened(path);
        }

        _view.SetTabDisplaySizeLabel(_view.TabDisplaySize);
        _view.SetIndentSizeLabel(_view.IndentSize);
        _view.SetIndentationInputModeLabel(_view.IndentationInputMode);
        _status.Refresh(args.LineEnding);
        _status.RefreshTitle();
        RefreshExternalToolsMenu();
        _refreshWindowMenus();
    }

    private void LanguageModes_Changed(object? sender, EventArgs args)
    {
        _status.Refresh();
        RefreshExternalToolsMenu();
    }

    private void Session_StateChanged(object? sender, EventArgs args)
    {
        _documents.StopFileWatcherIfPathChanged();
        if (_session.State.FilePath is { } path)
        {
            _languageModes.SelectForPath(path);
        }

        RefreshDocumentView();
    }

    private ExternalToolMenuState GetExternalToolMenuState(ExternalToolSettings tool)
    {
        var context = CreateExternalToolContext(tool.DefinitionDirectory);
        return ExternalToolAvailability.Evaluate(tool, context);
    }

    private ExternalToolContext CreateExternalToolContext(
        string? toolDirectory = null,
        string? filePathOverride = null)
    {
        var editorSnapshot = EditorBufferSnapshot.Capture(_view);
        var state = _session.State;
        var filePath = filePathOverride ?? state.FilePath;
        return new ExternalToolContext(
            filePath,
            filePath,
            editorSnapshot.Text,
            editorSnapshot.SelectedText,
            editorSnapshot.Caret.Line + 1,
            editorSnapshot.Caret.Column + 1,
            _languageModes.CurrentModeId,
            toolDirectory,
            state.Encoding,
            state.LineEnding,
            state.IsDirty,
            editorSnapshot.SelectionStart,
            editorSnapshot.SelectionEnd);
    }

    private static ShellCommandInvocation ResolveShellCommand(
        ShellCommandSettings settings,
        string sectionName,
        ExternalToolContext context,
        string defaultWorkingDirectory)
    {
        settings.Validate(sectionName);
        var command = context.Expand(settings.Command);
        var arguments = context.Expand(settings.Arguments);
        var workingDirectory = string.IsNullOrWhiteSpace(settings.WorkingDirectory)
            ? defaultWorkingDirectory
            : context.Expand(settings.WorkingDirectory);
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            workingDirectory = defaultWorkingDirectory;
        }

        if (!Path.IsPathRooted(workingDirectory))
        {
            workingDirectory = Path.GetFullPath(workingDirectory, defaultWorkingDirectory);
        }

        return new ShellCommandInvocation(command, arguments, workingDirectory);
    }

    private sealed record ShellCommandInvocation(
        string Command,
        string[] Arguments,
        string WorkingDirectory);

    private void RefreshDocumentView()
    {
        _status.Refresh();
        _status.RefreshTitle();
        RefreshExternalToolsMenu();
        _refreshWindowMenus();
    }
}
