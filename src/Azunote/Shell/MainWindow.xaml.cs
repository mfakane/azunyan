using Azunyan.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace Azunote;

public sealed partial class MainWindow : Window
{
    private readonly IntPtr _windowHandle;
    private readonly AppWindow? _appWindow;
    private readonly ExternalToolRunner _externalToolRunner = new();
    private readonly string _settingsDirectory = SettingsFileService.GetDefaultDirectory();
    private string? _filePath;
    private string _savedText = string.Empty;
    private TextEncodingKind _encoding = TextEncodingKind.Utf8;
    private LineEndingKind _lineEnding = GetDefaultLineEnding();
    private AzunoteSettings _settings = new();
    private bool _isLoading;
    private bool _allowClose;
    private bool _wordWrapEnabled;
    private FileSystemWatcher? _fileWatcher;
    private FileSystemWatcher? _settingsWatcher;
    private CancellationTokenSource? _fileChangeDebounce;
    private CancellationTokenSource? _settingsChangeDebounce;
    private bool _externalChangeDialogOpen;

    public MainWindow()
    {
        InitializeComponent();
        Editor.Providers.Syntax = new AzunoteSyntaxProvider();
        Editor.Providers.Completion = new AzunoteCompletionProvider();
        Editor.Providers.Tooltip = new AzunoteTooltipProvider();
        Editor.Providers.Folding = new AzunoteFoldingProvider();
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
        var open = new KeyboardAccelerator { Key = VirtualKey.O, Modifiers = VirtualKeyModifiers.Control };
        open.Invoked += OpenAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(open);

        var save = new KeyboardAccelerator { Key = VirtualKey.S, Modifiers = VirtualKeyModifiers.Control };
        save.Invoked += SaveAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(save);

        var saveAs = new KeyboardAccelerator { Key = VirtualKey.S, Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift };
        saveAs.Invoked += SaveAsAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(saveAs);

        var find = new KeyboardAccelerator { Key = VirtualKey.F, Modifiers = VirtualKeyModifiers.Control };
        find.Invoked += FindAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(find);

        var replace = new KeyboardAccelerator { Key = VirtualKey.H, Modifiers = VirtualKeyModifiers.Control };
        replace.Invoked += ReplaceAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(replace);

        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += EscapeAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(escape);

        var newDocument = new KeyboardAccelerator { Key = VirtualKey.N, Modifiers = VirtualKeyModifiers.Control };
        newDocument.Invoked += NewAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(newDocument);
    }

    private bool IsDirty => !string.Equals(Editor.Text, _savedText, StringComparison.Ordinal);

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

    public async Task OpenStartupTextAsync(
        string text,
        int? line = null,
        int? column = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        _isLoading = true;
        try
        {
            StopFileWatcher();
            Editor.SetText(text);
        }
        finally
        {
            _isLoading = false;
        }

        _filePath = null;
        _savedText = string.Empty;
        _encoding = TextEncodingKind.Utf8;
        _lineEnding = GetLineEndingOrDefault(TextFileService.DetectLineEnding(text));
        UpdateStatus();
        UpdateTitle();
        SetStartupPosition(line, column);
        Editor.Focus(FocusState.Programmatic);
    }

    public async Task InitializeSettingsAsync()
    {
        try
        {
            await SettingsFileService.EnsureExistsAsync(_settingsDirectory);
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
    /// the current file; a failed process never changes the document.
    /// </summary>
    public async Task<ExternalToolResult> RunExternalToolAsync(
        ExternalToolDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var selection = new TextSelection(Editor.SelectionStart, Editor.SelectionStart + Editor.SelectionLength);
        var caret = Editor.Document.CaretPosition;
        var lineColumn = Editor.Snapshot.Lines.GetLineColumn(caret);
        var context = new ExternalToolContext(
            _filePath,
            Editor.Text,
            Editor.SelectedText,
            lineColumn.Line + 1,
            lineColumn.Column + 1);
        ExternalToolResult result;
        try
        {
            result = await _externalToolRunner.RunAsync(definition, context, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowErrorAsync("Could not run external tool", exception.Message);
            throw;
        }

        var output = ExternalToolOutputInterpreter.Interpret(definition, result);
        if (!output.IsSuccess)
        {
            await ShowErrorAsync("External tool failed", output.Error!);
            return result;
        }

        if (output.ReloadFile)
        {
            if (_filePath is null)
            {
                await ShowErrorAsync(
                    "Could not reload file",
                    "The current document is not backed by a file.");
            }
            else
            {
                if (IsDirty)
                {
                    var changedDocument = await TextFileService.ReadAsync(_filePath);
                    await ResolveExternalFileConflictAsync(
                        changedDocument,
                        CancellationToken.None);
                }
                else
                {
                    await ReloadDocumentFromDiskAsync();
                }
            }

            return result;
        }

        if (output.ReplacementText is not null)
        {
            switch (definition.OutputMode)
            {
                case ExternalToolOutputMode.ReplaceDocument:
                    Editor.ReplaceDocumentRange(
                        new TextRange(0, Editor.Text.Length),
                        output.ReplacementText);
                    break;
                case ExternalToolOutputMode.ReplaceSelection:
                    if (selection.End > Editor.Text.Length)
                    {
                        await ShowErrorAsync(
                            "Could not apply external tool output",
                            "The selection changed while the tool was running.");
                    }
                    else
                    {
                        Editor.ReplaceDocumentRange(selection.Range, output.ReplacementText);
                    }

                    break;
                case ExternalToolOutputMode.NewDocument:
                    await OpenStartupTextAsync(output.ReplacementText);
                    break;
            }

            UpdateStatus();
            UpdateTitle();
        }

        return result;
    }

    private async void OpenButton_Click(object sender, RoutedEventArgs e) => await OpenFileAsync();

    private async void NewMenuItem_Click(object sender, RoutedEventArgs e) => await NewDocumentAsync();

    private void NewAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = NewDocumentAsync();
    }

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
            await SettingsFileService.EnsureExistsAsync(_settingsDirectory);
            var folder = await StorageFolder.GetFolderFromPathAsync(_settingsDirectory);
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

        if (_settings.ExternalToolMenu.Count == 0)
        {
            ConfiguredExternalToolsMenuItem.Items.Add(
                new MenuFlyoutItem
                {
                    Text = "No tools configured",
                    IsEnabled = false
                });
            return;
        }

        AddExternalToolMenuItems(ConfiguredExternalToolsMenuItem, _settings.ExternalToolMenu);
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
            var settings = await SettingsFileService.LoadAsync(_settingsDirectory);
            _settings = settings;
            RefreshExternalToolMenu();

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

        var directory = Path.GetFullPath(_settingsDirectory);
        if (!Directory.Exists(directory))
        {
            return;
        }

        _settingsWatcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite
                | NotifyFilters.Size
                | NotifyFilters.FileName
                | NotifyFilters.DirectoryName
        };
        _settingsWatcher.Changed += SettingsWatcher_Changed;
        _settingsWatcher.Created += SettingsWatcher_Changed;
        _settingsWatcher.Deleted += SettingsWatcher_Changed;
        _settingsWatcher.Renamed += SettingsWatcher_Renamed;
        _settingsWatcher.EnableRaisingEvents = true;
    }

    private void StopSettingsWatcher()
    {
        if (_settingsWatcher is null)
        {
            return;
        }

        _settingsWatcher.EnableRaisingEvents = false;
        _settingsWatcher.Changed -= SettingsWatcher_Changed;
        _settingsWatcher.Created -= SettingsWatcher_Changed;
        _settingsWatcher.Deleted -= SettingsWatcher_Changed;
        _settingsWatcher.Renamed -= SettingsWatcher_Renamed;
        _settingsWatcher.Dispose();
        _settingsWatcher = null;
    }

    private void SettingsWatcher_Changed(object sender, FileSystemEventArgs args) => QueueSettingsReload();

    private void SettingsWatcher_Renamed(object sender, RenamedEventArgs args) => QueueSettingsReload();

    private void QueueSettingsReload()
    {
        _settingsChangeDebounce?.Cancel();
        _settingsChangeDebounce?.Dispose();
        _settingsChangeDebounce = new CancellationTokenSource();
        var cancellationToken = _settingsChangeDebounce.Token;
        DispatcherQueue.TryEnqueue(() => _ = HandleSettingsChangedAsync(cancellationToken));
    }

    private async Task HandleSettingsChangedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(150, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await SettingsFileService.EnsureExistsAsync(_settingsDirectory, cancellationToken);

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

    private void OpenAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = OpenFileAsync();
    }

    private void SaveAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = SaveAsync();
    }

    private void SaveAsAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = SaveAsAsync();
    }

    private void FindAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ShowFindPanel(replace: false);
    }

    private void ReplaceAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ShowFindPanel(replace: true);
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

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _windowHandle);

        var file = await picker.PickSingleFileAsync();
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
            await ShowErrorAsync("Could not open the file", exception.Message);
        }
    }

    private async Task NewDocumentAsync()
    {
        if (!await ConfirmPendingChangesAsync())
        {
            return;
        }

        _isLoading = true;
        try
        {
            StopFileWatcher();
            Editor.SetText(string.Empty);
        }
        finally
        {
            _isLoading = false;
        }

        _filePath = null;
        _savedText = string.Empty;
        _encoding = TextEncodingKind.Utf8;
        _lineEnding = GetDefaultLineEnding();
        UpdateStatus();
        UpdateTitle();
        Editor.Focus(FocusState.Programmatic);
    }

    private async Task LoadDocumentAsync(string path)
    {
        var document = await TextFileService.ReadAsync(path);

        _isLoading = true;
        try
        {
            StopFileWatcher();
            Editor.SetText(document.Text);
        }
        finally
        {
            _isLoading = false;
        }

        _filePath = Path.GetFullPath(path);
        _savedText = document.Text;
        _encoding = document.Encoding;
        _lineEnding = GetLineEndingOrDefault(document.LineEnding);
        UpdateStatus(document.LineEnding);
        UpdateTitle();
        Editor.Focus(FocusState.Programmatic);
        Editor.SetDocumentSelection(TextSelection.Caret(0));
        StartFileWatcher(_filePath);
    }

    private async Task ReloadDocumentFromDiskAsync()
    {
        if (_filePath is null)
        {
            return;
        }

        var selection = new TextSelection(Editor.SelectionStart, Editor.SelectionStart + Editor.SelectionLength);
        var document = await TextFileService.ReadAsync(_filePath);
        _isLoading = true;
        try
        {
            StopFileWatcher();
            Editor.SetText(document.Text);
        }
        finally
        {
            _isLoading = false;
        }

        _savedText = document.Text;
        _encoding = document.Encoding;
        _lineEnding = GetLineEndingOrDefault(document.LineEnding);
        var restoredSelection = new TextSelection(
            Math.Min(selection.Anchor, document.Text.Length),
            Math.Min(selection.Active, document.Text.Length));
        Editor.SetDocumentSelection(restoredSelection);
        UpdateStatus(document.LineEnding);
        UpdateTitle();
        StartFileWatcher(_filePath);
    }

    private async Task<bool> SaveAsync()
    {
        if (_filePath is null)
        {
            return await SaveAsAsync();
        }

        try
        {
            await TextFileService.WriteAsync(_filePath, Editor.Text, _encoding, _lineEnding);
            _savedText = Editor.Text;
            UpdateStatus(_lineEnding);
            UpdateTitle();
            StartFileWatcher(_filePath);
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
                _filePath is null ? "Untitled.txt" : Path.GetFileName(_filePath),
                _encoding,
                GetLineEndingOrDefault(_lineEnding));
            if (save is null)
            {
                return false;
            }

            await TextFileService.WriteAsync(
                save.Path,
                Editor.Text,
                save.Encoding,
                save.LineEnding);
            _filePath = Path.GetFullPath(save.Path);
            _savedText = Editor.Text;
            _encoding = save.Encoding;
            _lineEnding = save.LineEnding;
            UpdateStatus(_lineEnding);
            UpdateTitle();
            StartFileWatcher(_filePath);
            return true;
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Could not save the file", exception.Message);
            return false;
        }
    }

    private async Task<bool> ConfirmPendingChangesAsync()
    {
        if (!IsDirty)
        {
            return true;
        }

        var dialog = new ContentDialog
        {
            Title = "Save changes?",
            Content = "The current document has unsaved changes.",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Don't save",
            CloseButtonText = "Cancel",
            XamlRoot = RootGrid.XamlRoot
        };

        var result = await dialog.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary => await SaveAsync(),
            ContentDialogResult.Secondary => true,
            _ => false
        };
    }

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

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _fileChangeDebounce?.Cancel();
        _fileChangeDebounce?.Dispose();
        _fileChangeDebounce = null;
        _settingsChangeDebounce?.Cancel();
        _settingsChangeDebounce?.Dispose();
        _settingsChangeDebounce = null;
        StopFileWatcher();
        StopSettingsWatcher();
    }

    private void StartFileWatcher(string path)
    {
        StopFileWatcher();

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        _fileWatcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite
                | NotifyFilters.Size
                | NotifyFilters.FileName
        };
        _fileWatcher.Changed += FileWatcher_Changed;
        _fileWatcher.Created += FileWatcher_Changed;
        _fileWatcher.Renamed += FileWatcher_Renamed;
        _fileWatcher.EnableRaisingEvents = true;
    }

    private void StopFileWatcher()
    {
        if (_fileWatcher is null)
        {
            return;
        }

        _fileWatcher.EnableRaisingEvents = false;
        _fileWatcher.Changed -= FileWatcher_Changed;
        _fileWatcher.Created -= FileWatcher_Changed;
        _fileWatcher.Renamed -= FileWatcher_Renamed;
        _fileWatcher.Dispose();
        _fileWatcher = null;
    }

    private void FileWatcher_Changed(object sender, FileSystemEventArgs args) => QueueFileReload();

    private void FileWatcher_Renamed(object sender, RenamedEventArgs args) => QueueFileReload();

    private void QueueFileReload()
    {
        _fileChangeDebounce?.Cancel();
        _fileChangeDebounce?.Dispose();
        _fileChangeDebounce = new CancellationTokenSource();
        var cancellationToken = _fileChangeDebounce.Token;
        DispatcherQueue.TryEnqueue(() => _ = HandleFileChangedAsync(cancellationToken));
    }

    private async Task HandleFileChangedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(150, cancellationToken);
            if (cancellationToken.IsCancellationRequested || _filePath is null || !File.Exists(_filePath))
            {
                return;
            }

            var document = await TextFileService.ReadAsync(_filePath, cancellationToken);
            if (string.Equals(document.Text, _savedText, StringComparison.Ordinal))
            {
                return;
            }

            if (IsDirty)
            {
                await ResolveExternalFileConflictAsync(document, cancellationToken);
            }
            else
            {
                await ApplyExternalFileChangeAsync(document);
            }
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

    private async Task ResolveExternalFileConflictAsync(
        TextFileData document,
        CancellationToken cancellationToken)
    {
        if (_externalChangeDialogOpen || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        _externalChangeDialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = "File changed externally",
                Content = "The file changed outside Azunote while this document has unsaved changes.",
                PrimaryButtonText = "Reload file",
                SecondaryButtonText = "Keep my changes",
                CloseButtonText = "Cancel",
                XamlRoot = RootGrid.XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ApplyExternalFileChangeAsync(document);
            }
        }
        finally
        {
            _externalChangeDialogOpen = false;
        }
    }

    private async Task ApplyExternalFileChangeAsync(TextFileData document)
    {
        if (_filePath is null)
        {
            return;
        }

        var selection = new TextSelection(Editor.SelectionStart, Editor.SelectionStart + Editor.SelectionLength);
        _isLoading = true;
        try
        {
            StopFileWatcher();
            Editor.SetText(document.Text);
        }
        finally
        {
            _isLoading = false;
        }

        _savedText = document.Text;
        _encoding = document.Encoding;
        _lineEnding = GetLineEndingOrDefault(document.LineEnding);
        Editor.SetDocumentSelection(new TextSelection(
            Math.Min(selection.Anchor, document.Text.Length),
            Math.Min(selection.Active, document.Text.Length)));
        UpdateStatus(document.LineEnding);
        UpdateTitle();
        StartFileWatcher(_filePath);
        await Task.CompletedTask;
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

        UpdateStatus();
        UpdateTitle();
    }

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e) => UpdateStatus();

    private void Editor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && FindPanel.Visibility == Visibility.Visible && FindTextBox.FocusState != FocusState.Unfocused)
        {
            FindNext();
            e.Handled = true;
        }
    }

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

        var comparison = StringComparison.CurrentCultureIgnoreCase;
        var count = 0;
        var position = 0;
        while ((position = Editor.Text.IndexOf(query, position, comparison)) >= 0)
        {
            count++;
            position += query.Length;
        }

        if (count > 0)
        {
            Editor.Text = Editor.Text.Replace(query, ReplaceTextBox.Text, comparison);
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

        if (Editor.SelectionLength > 0 && string.Equals(Editor.SelectedText, query, StringComparison.CurrentCultureIgnoreCase))
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

        var comparison = StringComparison.CurrentCultureIgnoreCase;
        var start = Editor.SelectionStart + Editor.SelectionLength;
        var index = Editor.Text.IndexOf(query, start, comparison);
        if (index < 0 && start > 0)
        {
            index = Editor.Text.IndexOf(query, 0, comparison);
        }

        if (index < 0)
        {
            FindResultText.Text = "Not found";
            return;
        }

        Editor.Focus(FocusState.Programmatic);
        Editor.Select(index, query.Length);
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
        EncodingStatus.Text = TextFileService.GetEncodingDisplayName(_encoding);
        LineEndingStatus.Text = TextFileService.GetLineEndingDisplayName(lineEnding ?? _lineEnding);
        IndentationStatus.Text = TextEditorCommands.GetIndentationSettings(
            snapshot,
            selectionStart).DisplayName;
        FilePathStatus.Text = _filePath ?? "Untitled";
    }

    private static LineEndingKind GetDefaultLineEnding() =>
        OperatingSystem.IsWindows() ? LineEndingKind.CrLf : LineEndingKind.Lf;

    private static LineEndingKind GetLineEndingOrDefault(LineEndingKind lineEnding) =>
        lineEnding is LineEndingKind.CrLf or LineEndingKind.Lf or LineEndingKind.Cr
            ? lineEnding
            : GetDefaultLineEnding();

    private void UpdateTitle()
    {
        var name = _filePath is null ? "Untitled" : Path.GetFileName(_filePath);
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
