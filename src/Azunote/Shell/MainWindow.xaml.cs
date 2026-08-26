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

public sealed partial class MainWindow : Window
{
    private readonly IntPtr _windowHandle;
    private readonly AppWindow? _appWindow;
    private readonly ExternalToolRunner _externalToolRunner = new();
    private readonly Dictionary<string, ISyntaxProvider?> _languageModes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> _languageModeCompletionTriggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> _languageModeExtensions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> _languageModePatterns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToggleMenuFlyoutItem> _languageModeItems = new(StringComparer.OrdinalIgnoreCase);
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
    private string _languageModeId = "plain-text";
    private bool _languageModeManuallySelected;
    private bool _completionShortcutInvoked;

    public MainWindow()
    {
        InitializeComponent();
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
    /// the current file; a failed process never changes the document. An
    /// unsaved buffer is written to an extension-preserving temporary file so
    /// file path inputs and placeholders refer to the current editor text.
    /// </summary>
    public async Task<ExternalToolResult> RunExternalToolAsync(
        ExternalToolDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var selection = new TextSelection(Editor.SelectionStart, Editor.SelectionStart + Editor.SelectionLength);
        var caret = Editor.Document.CaretPosition;
        var lineColumn = Editor.Snapshot.Lines.GetLineColumn(caret);
        var temporaryFilePath = IsDirty || _filePath is null
            ? CreateExternalToolTemporaryFilePath()
            : null;
        ExternalToolResult result;
        try
        {
            if (temporaryFilePath is not null)
            {
                await TextFileService.WriteAsync(
                    temporaryFilePath,
                    Editor.Text,
                    _encoding,
                    _lineEnding,
                    cancellationToken);
            }

            var context = new ExternalToolContext(
                temporaryFilePath ?? _filePath,
                Editor.Text,
                Editor.SelectedText,
                lineColumn.Line + 1,
                lineColumn.Column + 1);
            result = await _externalToolRunner.RunAsync(definition, context, cancellationToken);

            var output = ExternalToolOutputInterpreter.Interpret(definition, result);
            if (!output.IsSuccess)
            {
                await ShowErrorAsync("External tool failed", output.Error!);
                return result;
            }

            if (output.ReloadFile)
            {
                if (temporaryFilePath is not null)
                {
                    await ReloadDocumentFromTemporaryFileAsync(temporaryFilePath);
                }
                else if (_filePath is null)
                {
                    await ShowErrorAsync(
                        "Could not reload file",
                        "The current document is not backed by a file.");
                }
                else
                {
                    if (IsDirty)
                    {
                        var changedDocument = await TextFileService.ReadAsync(_filePath, cancellationToken);
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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowErrorAsync("Could not run external tool", exception.Message);
            throw;
        }
        finally
        {
            DeleteExternalToolTemporaryFile(temporaryFilePath);
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
        LanguageModeMenuItem.Items.Clear();
        _languageModes.Clear();
        _languageModeCompletionTriggers.Clear();
        _languageModeExtensions.Clear();
        _languageModePatterns.Clear();
        _languageModeItems.Clear();

        AddLanguageMode(
            "plain-text",
            "Plain Text",
            null,
            fileExtensions: [".txt", ".log"],
            patterns: ["*.txt", "*.log"]);
        LanguageModeMenuItem.Items.Add(new MenuFlyoutSeparator());
        AddLanguageMode(
            "azunote",
            "Azunote",
            new AzunoteSyntaxProvider(),
            [".", "(", "{", "[", "->"],
            fileExtensions: [".toml"],
            patterns: AzunoteLanguageDefinition.Patterns);
        foreach (var language in BuiltInSyntaxLanguages.All)
        {
            AddLanguageMode(
                language.Id,
                language.DisplayName,
                language,
                language.CompletionTriggerCharacters,
                patterns: language.Patterns);
        }

        var additionalModes = customModes?
            .Where(language => !_languageModes.ContainsKey(language.Id))
            .ToArray() ?? Array.Empty<SyntaxLanguageDefinition>();
        if (additionalModes.Length > 0)
        {
            LanguageModeMenuItem.Items.Add(new MenuFlyoutSeparator());
            foreach (var language in additionalModes)
            {
                AddLanguageMode(
                    language.Id,
                    language.DisplayName,
                    language,
                    language.CompletionTriggerCharacters,
                    patterns: language.Patterns);
            }
        }

        if (!_languageModes.ContainsKey(selectedMode))
        {
            selectedMode = "plain-text";
        }

        SetLanguageMode(selectedMode, refresh: customModes is not null);
    }

    private void AddLanguageMode(
        string id,
        string displayName,
        ISyntaxProvider? provider,
        IReadOnlyList<string>? completionTriggerCharacters = null,
        IReadOnlyList<string>? fileExtensions = null,
        IReadOnlyList<string>? patterns = null)
    {
        _languageModes.Add(id, provider);
        _languageModeCompletionTriggers.Add(
            id,
            completionTriggerCharacters ?? Array.Empty<string>());
        _languageModeExtensions.Add(
            id,
            fileExtensions
                ?? (provider as SyntaxLanguageDefinition)?.FileExtensions
                ?? Array.Empty<string>());
        _languageModePatterns.Add(
            id,
            patterns
                ?? (provider as SyntaxLanguageDefinition)?.Patterns
                ?? Array.Empty<string>());
        var item = new ToggleMenuFlyoutItem
        {
            Text = displayName,
            Tag = id
        };
        item.Click += LanguageModeItem_Click;
        _languageModeItems.Add(id, item);
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
        if (!_languageModes.TryGetValue(id, out var provider))
        {
            return;
        }

        _languageModeId = id;
        Editor.CompletionTriggerCharacters =
            _languageModeCompletionTriggers.TryGetValue(id, out var completionTriggers)
                ? completionTriggers
                : Array.Empty<string>();
        var isAzunote = string.Equals(id, "azunote", StringComparison.OrdinalIgnoreCase);
        var azunoteSchemas = isAzunote
            ? AzunoteSchemaCatalog.ForPath(_filePath)
            : Array.Empty<AzunoteSchemaDefinition>();
        Editor.Providers.Syntax = isAzunote
            ? new AzunoteSyntaxProvider()
            : provider;
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
        var selectedModeId = "plain-text";
        var bestScore = -1;
        foreach (var pair in _languageModePatterns)
        {
            var score = SyntaxLanguageDefinition.GetPatternMatchScore(path, pair.Value);
            if (score > bestScore)
            {
                bestScore = score;
                selectedModeId = pair.Key;
            }
        }

        if (!string.Equals(_languageModeId, selectedModeId, StringComparison.OrdinalIgnoreCase))
        {
            SetLanguageMode(selectedModeId, refresh: true);
        }
    }

    private IReadOnlyList<FileDialogFilter> GetFileDialogFilters()
    {
        var modeFilters = _languageModes
            .Select(pair => new FileDialogFilter(
                pair.Key,
                _languageModeItems[pair.Key].Text,
                _languageModeExtensions.TryGetValue(pair.Key, out var extensions)
                    ? NormalizeFileExtensions(extensions)
                    : Array.Empty<string>()))
            .ToArray();
        var supportedExtensions = NormalizeFileExtensions(
            modeFilters.SelectMany(filter => filter.Extensions));

        return
        [
            new FileDialogFilter(
                "supported",
                "Supported files",
                supportedExtensions),
            ..modeFilters,
            new FileDialogFilter("all", "All files", ["*"])
        ];
    }

    private static IReadOnlyList<string> NormalizeFileExtensions(
        IEnumerable<string> extensions)
    {
        return extensions
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Select(extension => extension.Trim())
            .Select(extension => extension is "*" or "*.*"
                ? "*"
                : extension.StartsWith('*')
                    ? NormalizeFileExtension(extension[1..])
                    : NormalizeFileExtension(extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeFileExtension(string extension)
    {
        return extension.StartsWith('.') ? extension : $".{extension}";
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
            InitializeLanguageModeMenu(settings.CustomSyntaxModes);
            if (!_languageModeManuallySelected && _filePath is not null)
            {
                SelectLanguageModeForPath(_filePath);
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
        _languageModeManuallySelected = false;
        SelectLanguageModeForPath(_filePath);
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

    private async Task ReloadDocumentFromTemporaryFileAsync(string path)
    {
        if (!File.Exists(path))
        {
            await ShowErrorAsync(
                "Could not reload file",
                "The external tool did not leave the temporary file available.");
            return;
        }

        var selection = new TextSelection(Editor.SelectionStart, Editor.SelectionStart + Editor.SelectionLength);
        var document = await TextFileService.ReadAsync(path);
        _isLoading = true;
        try
        {
            Editor.SetText(document.Text);
        }
        finally
        {
            _isLoading = false;
        }

        // The temporary file represents the current buffer, not the saved
        // version on disk. Keep _savedText unchanged so the document remains
        // dirty when it was dirty before the tool ran.
        _encoding = document.Encoding;
        _lineEnding = GetLineEndingOrDefault(document.LineEnding);
        Editor.SetDocumentSelection(new TextSelection(
            Math.Min(selection.Anchor, document.Text.Length),
            Math.Min(selection.Active, document.Text.Length)));
        UpdateStatus(document.LineEnding);
        UpdateTitle();
    }

    private string CreateExternalToolTemporaryFilePath()
    {
        var extension = _filePath is null ? ".txt" : Path.GetExtension(_filePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".txt";
        }

        return Path.Combine(
            Path.GetTempPath(),
            $"azunote-external-{Guid.NewGuid():N}{extension}");
    }

    private static void DeleteExternalToolTemporaryFile(string? path)
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
            // Cleanup must not hide the external tool result.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup must not hide the external tool result.
        }
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
                GetLineEndingOrDefault(_lineEnding),
                GetFileDialogFilters(),
                _languageModeId);
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
