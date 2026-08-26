using Azunyan.Core;
using Azunyan.Syntax;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;

namespace Azunote;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly IntPtr _windowHandle;
    private readonly AppWindow? _appWindow;
    private readonly DocumentSession _documentSession = new();
    private readonly TextFileStore _textFileStore = new();
    private LanguageModeCatalog _languageModeCatalog = LanguageModeCatalog.Create();
    private readonly Dictionary<string, ToggleMenuFlyoutItem> _languageModeItems = new(StringComparer.OrdinalIgnoreCase);
    private readonly SettingsController _settingsController = new(
        SettingsFileService.GetDefaultDirectory());
    private IEditorBuffer _editorBuffer = null!;
    private DocumentController _documentController = null!;
    private ExternalToolController _externalToolController = null!;
    private IUserPrompt _userPrompt = null!;
    private bool _isLoading;
    private bool _allowClose;
    private bool _wordWrapEnabled;
    private DebouncedFileChangeMonitor? _fileChangeMonitor;
    private DebouncedFileChangeMonitor? _settingsChangeMonitor;
    private bool _externalChangeDialogOpen;
    private string _languageModeId = "plain-text";
    private bool _languageModeManuallySelected;
    private bool _completionShortcutInvoked;
    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();
        _userPrompt = new WinUiUserPrompt(() => RootGrid.XamlRoot);
        _editorBuffer = new AzunyanEditorBuffer(Editor);
        _documentController = new DocumentController(
            _editorBuffer,
            _documentSession,
            _textFileStore,
            _userPrompt);
        _externalToolController = new ExternalToolController(
            _editorBuffer,
            _documentController,
            _textFileStore,
            _userPrompt);
        InitializeLanguageModeMenu();
        Editor.ColorScheme = AzunoteSystemColorScheme.CreateLight();
        RegisterKeyboardAccelerators();

        _windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        if (_appWindow is not null)
        {
            _appWindow.Closing += AppWindow_Closing;
        }

        Closed += MainWindow_Closed;

        UpdateStatus();
        UpdateTitle();
    }

    private void RegisterKeyboardAccelerators()
    {
        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += EscapeAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(escape);
    }

    private bool IsDirty => _documentSession.State.IsDirty;

    private string? CurrentFilePath => _documentSession.State.FilePath;

    private TextEncodingKind CurrentEncoding => _documentSession.State.Encoding;

    private LineEndingKind CurrentLineEnding => _documentSession.State.LineEnding;

    public async Task OpenStartupDocumentAsync(
        string path,
        int? line = null,
        int? column = null)
    {
        try
        {
            await LoadDocumentAsync(path);
            SetStartupPosition(line, column);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Could not open the file", exception.Message);
        }
    }

    public Task OpenStartupTextAsync(
        string text,
        int? line = null,
        int? column = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        StopFileWatcher();
        _isLoading = true;
        try
        {
            _documentController.LoadUntitledText(text);
        }
        finally
        {
            _isLoading = false;
        }
        UpdateStatus();
        UpdateTitle();
        SetStartupPosition(line, column);
        Editor.Focus(FocusState.Programmatic);
        return Task.CompletedTask;
    }

    public async Task InitializeSettingsAsync()
    {
        try
        {
            await _settingsController.EnsureExistsAsync();
            await TryLoadSettingsAsync(showError: true);
            StartSettingsWatcher();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Could not initialize settings", exception.Message);
        }
    }

    /// <summary>
    /// Runs a configured process against the current editor context. The
    /// caller chooses whether stdout is ignored, inserted, or used to reload
    /// the current file; a failed process never changes the document. An
    /// unsaved buffer is written to an extension-preserving temporary file so
    /// file path inputs and placeholders refer to the current editor text.
    /// </summary>
    public async Task<ExternalToolResult> RunExternalToolAsync(
        ExternalToolDefinition definition,
        CancellationToken cancellationToken = default)
    {
        StopFileWatcher();
        try
        {
            return await _externalToolController.RunAsync(definition, cancellationToken);
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

            UpdateStatus();
            UpdateTitle();
        }
    }

    private async void OpenButton_Click(object sender, RoutedEventArgs e) => await OpenFileAsync();

    private async void NewMenuItem_Click(object sender, RoutedEventArgs e) => await NewDocumentAsync();

    private async void SaveButton_Click(object sender, RoutedEventArgs e) => await SaveAsync();

    private async void SaveAsButton_Click(object sender, RoutedEventArgs e) => await SaveAsAsync();

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => Close();

    private void UndoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Editor.Focus(FocusState.Programmatic);
        Editor.UndoDocument();
    }

    private void RedoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Editor.Focus(FocusState.Programmatic);
        Editor.RedoDocument();
    }

    private void CutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Editor.Focus(FocusState.Programmatic);
        Editor.CutSelectionToClipboard();
    }

    private void CopyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Editor.Focus(FocusState.Programmatic);
        Editor.CopySelectionToClipboard();
    }

    private void PasteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Editor.Focus(FocusState.Programmatic);
        Editor.PasteFromClipboard();
    }

    private void SelectAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Editor.Focus(FocusState.Programmatic);
        Editor.SelectAll();
    }

    private void WordWrapMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _wordWrapEnabled = !_wordWrapEnabled;
        Editor.TextWrapping = _wordWrapEnabled ? TextWrapping.Wrap : TextWrapping.NoWrap;
        WordWrapMenuItem.Text = _wordWrapEnabled ? "Word Wrap ✓" : "Word Wrap";
    }

    private void StatusBarMenuItem_Click(object sender, RoutedEventArgs e)
    {
        StatusBarPanel.Visibility = StatusBarPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void InitializeLanguageModeMenu(
        IReadOnlyList<SyntaxLanguageDefinition>? customModes = null)
    {
        var selectedMode = _languageModeId;
        _languageModeCatalog = LanguageModeCatalog.Create(customModes);
        LanguageModeMenuItem.Items.Clear();
        _languageModeItems.Clear();

        AddLanguageMode(_languageModeCatalog.Entries[0]);
        LanguageModeMenuItem.Items.Add(new MenuFlyoutSeparator());
        AddLanguageMode(_languageModeCatalog.Entries[1]);
        for (var index = 2; index < _languageModeCatalog.Entries.Count; index++)
        {
            if (index == _languageModeCatalog.CustomModeStartIndex)
            {
                LanguageModeMenuItem.Items.Add(new MenuFlyoutSeparator());
            }

            AddLanguageMode(_languageModeCatalog.Entries[index]);
        }

        if (!_languageModeCatalog.TryGet(selectedMode, out _))
        {
            selectedMode = "plain-text";
        }

        SetLanguageMode(selectedMode, refresh: customModes is not null);
    }

    private void AddLanguageMode(LanguageModeEntry mode)
    {
        var item = new ToggleMenuFlyoutItem
        {
            Text = mode.DisplayName,
            Tag = mode.Id
        };
        item.Click += LanguageModeItem_Click;
        _languageModeItems.Add(mode.Id, item);
        LanguageModeMenuItem.Items.Add(item);
    }

    private void LanguageModeItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem item && item.Tag is string id)
        {
            _languageModeManuallySelected = true;
            SetLanguageMode(id, refresh: true);
        }
    }

    private void SetLanguageMode(string id, bool refresh)
    {
        if (!_languageModeCatalog.TryGet(id, out var mode))
        {
            return;
        }

        _languageModeId = id;
        Editor.CompletionTriggerCharacters = mode.CompletionTriggers;
        var isAzunote = string.Equals(id, "azunote", StringComparison.OrdinalIgnoreCase);
        var azunoteSchemas = isAzunote
            ? AzunoteSchemaCatalog.ForPath(CurrentFilePath)
            : Array.Empty<AzunoteSchemaDefinition>();
        Editor.Providers.Syntax = isAzunote
            ? new AzunoteSyntaxProvider()
            : mode.Provider;
        Editor.Providers.Completion = isAzunote
            ? new AzunoteCompletionProvider(azunoteSchemas)
            : null;
        Editor.Providers.Tooltip = null;
        Editor.Providers.Folding = null;
        foreach (var pair in _languageModeItems)
        {
            pair.Value.IsChecked = string.Equals(pair.Key, id, StringComparison.OrdinalIgnoreCase);
        }

        if (refresh)
        {
            Editor.RefreshProviders();
        }
    }

    private void SelectLanguageModeForPath(string path)
    {
        var selectedModeId = _languageModeCatalog.SelectForPath(path);

        if (!string.Equals(_languageModeId, selectedModeId, StringComparison.OrdinalIgnoreCase))
        {
            SetLanguageMode(selectedModeId, refresh: true);
        }
    }

    private IReadOnlyList<FileDialogFilter> GetFileDialogFilters()
        => _languageModeCatalog.GetFileDialogFilters();

    private async void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "About Azunote",
            Content = "Azunote\nA small WinUI 3 single-document text editor.",
            CloseButtonText = "OK",
            XamlRoot = RootGrid.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async void RunExternalToolMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var commandBox = new TextBox
        {
            Header = "Command",
            PlaceholderText = "clang-format, prettier, powershell..."
        };
        var argumentsBox = new TextBox
        {
            Header = "Arguments",
            PlaceholderText = "Use ${file}, ${fileDir}, or ${env:USERNAME}"
        };
        var inputModeBox = new ComboBox
        {
            Header = "Input",
            ItemsSource = Enum.GetNames<ExternalToolInputMode>(),
            SelectedIndex = 0
        };
        var outputModeBox = new ComboBox
        {
            Header = "Output",
            ItemsSource = Enum.GetNames<ExternalToolOutputMode>(),
            SelectedIndex = 0
        };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(commandBox);
        panel.Children.Add(argumentsBox);
        panel.Children.Add(inputModeBox);
        panel.Children.Add(outputModeBox);

        var dialog = new ContentDialog
        {
            Title = "Run External Tool",
            Content = panel,
            PrimaryButtonText = "Run",
            CloseButtonText = "Cancel",
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary
            || string.IsNullOrWhiteSpace(commandBox.Text))
        {
            return;
        }

        var inputMode = Enum.Parse<ExternalToolInputMode>(
            inputModeBox.SelectedItem?.ToString() ?? nameof(ExternalToolInputMode.None));
        var outputMode = Enum.Parse<ExternalToolOutputMode>(
            outputModeBox.SelectedItem?.ToString() ?? nameof(ExternalToolOutputMode.Ignore));
        await RunExternalToolAsync(new ExternalToolDefinition(
            commandBox.Text,
            ExternalToolDefinition.ParseArguments(argumentsBox.Text),
            inputMode,
            outputMode));
    }

    private async void PreferencesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _settingsController.EnsureExistsAsync();
            var folder = await StorageFolder.GetFolderFromPathAsync(_settingsController.Directory);
            if (!await Launcher.LaunchFolderAsync(folder))
            {
                throw new InvalidOperationException("Windows could not open the settings folder.");
            }
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Could not open settings folder", exception.Message);
        }
    }

    private void RefreshExternalToolMenu()
    {
        ConfiguredExternalToolsMenuItem.Items.Clear();

        if (_settingsController.Current.ExternalToolMenu.Count == 0)
        {
            ConfiguredExternalToolsMenuItem.Items.Add(
                new MenuFlyoutItem
                {
                    Text = "No tools configured",
                    IsEnabled = false
                });
            return;
        }

        AddExternalToolMenuItems(
            ConfiguredExternalToolsMenuItem,
            _settingsController.Current.ExternalToolMenu);
    }

    private void AddExternalToolMenuItems(
        MenuFlyoutSubItem parent,
        IReadOnlyList<ExternalToolMenuNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Tool is { } tool)
            {
                var menuItem = new MenuFlyoutItem { Text = node.Name };
                menuItem.Click += (_, _) => _ = RunConfiguredExternalToolAsync(tool);
                parent.Items.Add(menuItem);
                continue;
            }

            var subMenu = new MenuFlyoutSubItem { Text = node.Name };
            AddExternalToolMenuItems(subMenu, node.Children);
            if (subMenu.Items.Count > 0)
            {
                parent.Items.Add(subMenu);
            }
        }
    }

    private async Task RunConfiguredExternalToolAsync(ExternalToolSettings tool)
    {
        try
        {
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

    private async Task<bool> TryLoadSettingsAsync(bool showError)
    {
        try
        {
            var settings = await _settingsController.LoadAsync();
            RefreshExternalToolMenu();
            InitializeLanguageModeMenu(settings.CustomSyntaxModes);
            if (!_languageModeManuallySelected && CurrentFilePath is not null)
            {
                SelectLanguageModeForPath(CurrentFilePath);
            }

            return true;
        }
        catch (Exception exception)
        {
            if (showError)
            {
                await ShowErrorAsync("Could not load settings", exception.Message);
            }

            return false;
        }
    }

    private void StartSettingsWatcher()
    {
        StopSettingsWatcher();

        var directory = Path.GetFullPath(_settingsController.Directory);
        if (!Directory.Exists(directory))
        {
            return;
        }

        _settingsChangeMonitor = new DebouncedFileChangeMonitor(
            directory,
            includeSubdirectories: true);
        _settingsChangeMonitor.Changed += SettingsWatcher_Changed;
    }

    private void StopSettingsWatcher()
    {
        if (_settingsChangeMonitor is null)
        {
            return;
        }

        _settingsChangeMonitor.Changed -= SettingsWatcher_Changed;
        _settingsChangeMonitor.Dispose();
        _settingsChangeMonitor = null;
    }

    private void SettingsWatcher_Changed(
        object? sender,
        FileChangeDetectedEventArgs args)
    {
        if (ReferenceEquals(sender, _settingsChangeMonitor))
        {
            QueueSettingsReload();
        }
    }

    private void QueueSettingsReload() =>
        DispatcherQueue.TryEnqueue(() => _ = HandleSettingsChangedAsync(CancellationToken.None));

    private async Task HandleSettingsChangedAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await _settingsController.EnsureExistsAsync(cancellationToken);

            await TryLoadSettingsAsync(showError: true);
        }
        catch (OperationCanceledException)
        {
            // A burst of settings notifications was superseded by a newer one.
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Could not reload settings", exception.Message);
        }
    }

    private void FindButton_Click(object sender, RoutedEventArgs e) => ShowFindPanel(replace: false);

    private void ReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (FindPanel.Visibility != Visibility.Visible)
        {
            ShowFindPanel(replace: true);
            return;
        }

        ReplaceCurrent();
    }

    private void ShowCompletionMenuItem_Click(object sender, RoutedEventArgs e) => ShowCompletion();

    private void ShowCompletionAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _completionShortcutInvoked = true;
        ShowCompletion();
    }

    private void ShowCompletion()
    {
        Editor.Focus(FocusState.Programmatic);
        Editor.RequestCompletion();
    }

    private void EscapeAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FindPanel.Visibility == Visibility.Visible)
        {
            args.Handled = true;
            FindPanel.Visibility = Visibility.Collapsed;
            Editor.Focus(FocusState.Programmatic);
        }
    }

    private async Task OpenFileAsync()
    {
        if (!await ConfirmPendingChangesAsync())
        {
            return;
        }

        try
        {
            var path = NativeOpenFileDialog.Show(
                _windowHandle,
                GetFileDialogFilters(),
                "supported");
            if (path is null)
            {
                return;
            }

            await LoadDocumentAsync(path);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Could not open the file", exception.Message);
        }
    }

    private async Task NewDocumentAsync()
    {
        if (!await ConfirmPendingChangesAsync())
        {
            return;
        }

        StopFileWatcher();
        _isLoading = true;
        try
        {
            _documentController.NewDocument();
        }
        finally
        {
            _isLoading = false;
        }

        UpdateStatus();
        UpdateTitle();
        Editor.Focus(FocusState.Programmatic);
    }

    private async Task LoadDocumentAsync(string path)
    {
        StopFileWatcher();
        _isLoading = true;
        try
        {
            await _documentController.OpenAsync(path);
        }
        finally
        {
            _isLoading = false;
        }

        _languageModeManuallySelected = false;
        if (CurrentFilePath is { } filePath)
        {
            SelectLanguageModeForPath(filePath);
        }

        UpdateStatus(TextFileService.DetectLineEnding(Editor.Text));
        UpdateTitle();
        Editor.Focus(FocusState.Programmatic);
        StartFileWatcher();
    }

    private async Task ReloadDocumentFromDiskAsync()
    {
        if (CurrentFilePath is null)
        {
            return;
        }

        StopFileWatcher();
        _isLoading = true;
        try
        {
            await _documentController.ReloadFromDiskAsync();
        }
        finally
        {
            _isLoading = false;
        }

        UpdateStatus(TextFileService.DetectLineEnding(Editor.Text));
        UpdateTitle();
        StartFileWatcher();
    }

    private async Task ReloadDocumentFromTemporaryFileAsync(string path)
    {
        if (!File.Exists(path))
        {
            await ShowErrorAsync(
                "Could not reload file",
                "The external tool did not leave the temporary file available.");
            return;
        }

        _isLoading = true;
        try
        {
            await _documentController.ReloadFromTemporaryFileAsync(path);
        }
        finally
        {
            _isLoading = false;
        }

        UpdateStatus(TextFileService.DetectLineEnding(Editor.Text));
        UpdateTitle();
    }

    private async Task<bool> SaveAsync()
    {
        if (CurrentFilePath is null)
        {
            return await SaveAsAsync();
        }

        try
        {
            await _documentController.SaveAsync();
            UpdateStatus();
            UpdateTitle();
            StartFileWatcher();
            return true;
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Could not save the file", exception.Message);
            return false;
        }
    }

    private async Task<bool> SaveAsAsync()
    {
        try
        {
            var save = NativeSaveFileDialog.Show(
                _windowHandle,
                CurrentFilePath is null ? "Untitled.txt" : Path.GetFileName(CurrentFilePath),
                CurrentEncoding,
                CurrentLineEnding,
                GetFileDialogFilters(),
                _languageModeId);
            if (save is null)
            {
                return false;
            }

            await _documentController.SaveAsAsync(save);
            UpdateStatus();
            UpdateTitle();
            StartFileWatcher();
            return true;
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Could not save the file", exception.Message);
            return false;
        }
    }

    private async Task<bool> ConfirmPendingChangesAsync()
        => await _documentController.ConfirmPendingChangesAsync(SaveAsync);

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose || !IsDirty)
        {
            return;
        }

        args.Cancel = true;
        if (!await ConfirmPendingChangesAsync())
        {
            return;
        }

        _allowClose = true;
        Close();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopFileWatcher();
        StopSettingsWatcher();
        Editor.Dispose();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args) => Dispose();

    private void StartFileWatcher()
    {
        StopFileWatcher();

        if (CurrentFilePath is not { } path)
        {
            return;
        }

        _fileChangeMonitor = new DebouncedFileChangeMonitor(path, includeSubdirectories: false);
        _fileChangeMonitor.Changed += FileWatcher_Changed;
    }

    private void StopFileWatcher()
    {
        if (_fileChangeMonitor is null)
        {
            return;
        }

        _fileChangeMonitor.Changed -= FileWatcher_Changed;
        _fileChangeMonitor.Dispose();
        _fileChangeMonitor = null;
    }

    private void FileWatcher_Changed(
        object? sender,
        FileChangeDetectedEventArgs args)
    {
        if (ReferenceEquals(sender, _fileChangeMonitor)
            && CurrentFilePath is { } path)
        {
            QueueFileReload(path);
        }
    }

    private void QueueFileReload(string expectedPath) =>
        DispatcherQueue.TryEnqueue(() =>
            _ = HandleFileChangedAsync(expectedPath, CancellationToken.None));

    private async Task HandleFileChangedAsync(
        string expectedPath,
        CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested
                || CurrentFilePath is not { } path
                || !string.Equals(
                    Path.GetFullPath(expectedPath),
                    Path.GetFullPath(path),
                    StringComparison.OrdinalIgnoreCase)
                || !File.Exists(path))
            {
                return;
            }

            var document = await _textFileStore.ReadAsync(path, cancellationToken);
            if (_documentSession.IsSameAsSaved(document.Text))
            {
                return;
            }

            await ApplyExternalFileChangeAsync(document, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A burst of filesystem notifications was superseded by a newer one.
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Could not reload the changed file", exception.Message);
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
            _isLoading = true;
            await _documentController.ApplyExternalChangeAsync(document, cancellationToken);
        }
        finally
        {
            _isLoading = false;
            _externalChangeDialogOpen = false;
        }

        UpdateStatus(TextFileService.DetectLineEnding(Editor.Text));
        UpdateTitle();
        StartFileWatcher();
    }

    private void SetStartupPosition(int? line, int? column)
    {
        if (line is null && column is null)
        {
            return;
        }

        var snapshot = Editor.Snapshot;
        var zeroBasedLine = Math.Clamp((line ?? 1) - 1, 0, snapshot.Lines.LineCount - 1);
        var zeroBasedColumn = Math.Max(0, (column ?? 1) - 1);
        var clampedColumn = Math.Min(zeroBasedColumn, snapshot.Lines.GetLineLength(zeroBasedLine));
        Editor.SetDocumentSelection(TextSelection.Caret(
            snapshot.Lines.GetPosition(new LineColumn(zeroBasedLine, clampedColumn))));
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        _documentSession.ObserveText(Editor.Text);
        UpdateStatus();
        UpdateTitle();
    }

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e) => UpdateStatus();

    private void Editor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // The menu accelerator invokes the command, but WinUI can still route
        // the accelerator's Space key to the native text host. Consume it at
        // the application boundary so it cannot insert a literal space. The
        // reusable Azunyan component remains unaware of this shortcut.
        if (e.Key == VirtualKey.Space && IsKeyDown(VirtualKey.Control))
        {
            // Normally the menu item's KeyboardAccelerator has already
            // invoked the command. Keep a fallback here for native text hosts
            // that route KeyDown without running the item accelerator first.
            var acceleratorInvoked = _completionShortcutInvoked;
            _completionShortcutInvoked = false;
            e.Handled = true;
            if (!acceleratorInvoked)
            {
                ShowCompletion();
            }

            return;
        }

        if (e.Key == VirtualKey.Enter && FindPanel.Visibility == Visibility.Visible && FindTextBox.FocusState != FocusState.Unfocused)
        {
            FindNext();
            e.Handled = true;
        }
    }

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(CoreVirtualKeyStates.Down);

    private void Editor_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private async void Editor_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems) || !await ConfirmPendingChangesAsync())
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        var file = items.FirstOrDefault(item => item is Windows.Storage.StorageFile);
        if (file is null)
        {
            return;
        }

        try
        {
            await LoadDocumentAsync(file.Path);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Could not open the dropped file", exception.Message);
        }
    }

    private void ShowFindPanel(bool replace)
    {
        FindPanel.Visibility = Visibility.Visible;
        FindTextBox.Focus(FocusState.Programmatic);
        if (Editor.SelectionLength > 0 && !string.IsNullOrEmpty(Editor.SelectedText))
        {
            FindTextBox.Text = Editor.SelectedText;
            FindTextBox.SelectAll();
        }

        if (!replace)
        {
            ReplaceTextBox.Text = string.Empty;
        }
    }

    private void CloseFindButton_Click(object sender, RoutedEventArgs e)
    {
        FindPanel.Visibility = Visibility.Collapsed;
        Editor.Focus(FocusState.Programmatic);
    }

    private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (FindPanel.Visibility == Visibility.Visible)
        {
            FindResultText.Text = string.Empty;
        }
    }

    private void FindTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            FindNext();
            e.Handled = true;
        }
    }

    private void ReplaceTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ReplaceCurrent();
            e.Handled = true;
        }
    }

    private void FindNextButton_Click(object sender, RoutedEventArgs e) => FindNext();

    private void ReplaceAllButton_Click(object sender, RoutedEventArgs e)
    {
        var query = FindTextBox.Text;
        if (string.IsNullOrEmpty(query))
        {
            FindResultText.Text = "Enter search text";
            return;
        }

        var count = FindReplaceService.Count(Editor.Text, query);

        if (count > 0)
        {
            Editor.Text = FindReplaceService.ReplaceAll(
                Editor.Text,
                query,
                ReplaceTextBox.Text);
        }

        FindResultText.Text = $"{count} replaced";
        UpdateStatus();
        UpdateTitle();
    }

    private void ReplaceCurrentButton_Click(object sender, RoutedEventArgs e)
    {
        ReplaceCurrent();
    }

    private void ReplaceCurrent()
    {
        var query = FindTextBox.Text;
        if (string.IsNullOrEmpty(query))
        {
            FindResultText.Text = "Enter search text";
            return;
        }

        if (Editor.SelectionLength > 0 && FindReplaceService.IsMatch(Editor.SelectedText, query))
        {
            var selectionStart = Editor.SelectionStart;
            Editor.SelectedText = ReplaceTextBox.Text;
            Editor.SelectionStart = selectionStart;
            Editor.SelectionLength = ReplaceTextBox.Text.Length;
            FindNext();
        }
        else
        {
            FindNext();
        }
    }

    private void FindNext()
    {
        var query = FindTextBox.Text;
        if (string.IsNullOrEmpty(query))
        {
            FindResultText.Text = "Enter search text";
            return;
        }

        var start = Editor.SelectionStart + Editor.SelectionLength;
        var match = FindReplaceService.FindNext(Editor.Text, query, start);

        if (match is not { } found)
        {
            FindResultText.Text = "Not found";
            return;
        }

        Editor.Focus(FocusState.Programmatic);
        Editor.Select(found.Start, found.Length);
        FindResultText.Text = "Found";
    }

    private void UpdateStatus(LineEndingKind? lineEnding = null)
    {
        var snapshot = Editor.Snapshot;
        var text = snapshot.Text;
        var selectionStart = Math.Clamp(Editor.SelectionStart, 0, text.Length);
        var lineColumn = snapshot.Lines.GetLineColumn(selectionStart);
        var line = lineColumn.Line + 1;
        var column = lineColumn.Column + 1;

        PositionStatus.Text = $"Ln {line}, Col {column}";
        EncodingStatus.Text = TextFileService.GetEncodingDisplayName(CurrentEncoding);
        LineEndingStatus.Text = TextFileService.GetLineEndingDisplayName(lineEnding ?? CurrentLineEnding);
        IndentationStatus.Text = TextEditorCommands.GetIndentationSettings(
            snapshot,
            selectionStart).DisplayName;
        FilePathStatus.Text = CurrentFilePath ?? "Untitled";
    }

    private void UpdateTitle()
    {
        var name = CurrentFilePath is null ? "Untitled" : Path.GetFileName(CurrentFilePath);
        var dirtyMarker = IsDirty ? "*" : string.Empty;
        if (_appWindow is not null)
        {
            _appWindow.Title = $"{dirtyMarker}{name} — Azunote";
        }
    }

    internal void ShowUnhandledError(Exception exception, string logPath)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);

        var detail = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;
        var message = $"{detail}\n\nDetails were written to:\n{logPath}";
        DispatcherQueue.TryEnqueue(() => _ = ShowErrorAsync("Azunote encountered an unexpected error", message));
    }

    internal Task ShowCommandLineHelpAsync() => ShowErrorAsync(
        "Azunote command line",
        AzunoteCommandLine.Usage);

    internal Task ShowStartupErrorAsync(string title, string message) => ShowErrorAsync(title, message);

    private async Task ShowErrorAsync(string title, string message)
    {
        try
        {
            if (RootGrid.XamlRoot is null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = RootGrid.XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch (Exception exception)
        {
            ErrorReporter.LogException("Error dialog failure", exception);
        }
    }
}
