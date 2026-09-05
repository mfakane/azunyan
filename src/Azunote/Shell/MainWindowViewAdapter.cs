using Azunyan.Core;
using Azunyan.WinUI;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinRT.Interop;
using Windows.Graphics;

namespace Azunote;

internal sealed class MainWindowViewAdapter :
    IEditorView,
    IWindowMenuView,
    IFindReplaceHost,
    ILanguageModeMenuView,
    IExternalToolMenuView
{
    private readonly AzunyanEditorView _editor;
    private readonly AzunyanEditorBuffer _editorBuffer;
    private readonly IntPtr _windowHandle;
    private readonly AppWindow? _appWindow;
    private readonly Grid _rootGrid;
    private readonly TextBox _findTextBox;
    private readonly MenuFlyoutSubItem _languageModeMenu;
    private readonly MenuFlyoutSubItem _openRecentMenu;
    private readonly MenuBarItem _windowMenu;
    private readonly MenuBarItem _toolsMenu;
    private readonly MenuFlyout _statusTabDisplaySizeMenu;
    private readonly MenuFlyout _statusIndentSizeMenu;
    private readonly MenuFlyout _statusFilePathMenu;
    private readonly TextBlock _indentationStatus;
    private readonly TextBlock _filePathStatus;
    private readonly List<KeyboardAccelerator> _externalToolAccelerators = [];
    private readonly List<MenuFlyoutItemBase> _externalToolMenuItems = [];
    private readonly List<MenuFlyoutItemBase> _externalToolContextMenuItems = [];
    private readonly List<MenuFlyoutItemBase> _recentFileMenuItems = [];
    private readonly List<MenuFlyoutItemBase> _windowMenuItems = [];
    private readonly MainWindowRadioGroupNames _radioGroups;
    private readonly MainWindowViewModel _viewModel;
    private readonly Dictionary<string, RadioMenuFlyoutItem> _languageModeItems =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Style _auxiliarySplitMenuFlyoutItemStyle;
    private bool _themeConfigured;

    internal Document Document => _editor.Document;

    internal void SetDocument(Document document) => _editor.SetDocument(document);

    public MainWindowViewAdapter(
        MainWindow window,
        MainWindowViewModel viewModel,
        AzunyanEditorView editor,
        Grid rootGrid,
        Style auxiliarySplitMenuFlyoutItemStyle,
        TextBox findTextBox,
        MenuFlyoutSubItem languageModeMenu,
        MenuFlyoutSubItem openRecentMenu,
        MenuBarItem windowMenu,
        MenuBarItem toolsMenu,
        MenuFlyout statusTabDisplaySizeMenu,
        MenuFlyout statusIndentSizeMenu,
        MenuFlyout statusFilePathMenu,
        TextBlock indentationStatus,
        TextBlock filePathStatus)
    {
        ArgumentNullException.ThrowIfNull(window);
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _radioGroups = viewModel.Groups;
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _editor.DiagnosticSink = (category, message) => ErrorReporter.LogMessage(
            $"Editor diagnostic/{AzunyanDiagnosticCategories.GetName(category)}",
            message);
        _editor.DiagnosticExceptionSink = (source, exception) => ErrorReporter.LogException(
            $"Editor exception/{source}",
            exception);
        _editorBuffer = new AzunyanEditorBuffer(editor);
        _rootGrid = rootGrid ?? throw new ArgumentNullException(nameof(rootGrid));
        _auxiliarySplitMenuFlyoutItemStyle = auxiliarySplitMenuFlyoutItemStyle
            ?? throw new ArgumentNullException(nameof(auxiliarySplitMenuFlyoutItemStyle));
        _findTextBox = findTextBox ?? throw new ArgumentNullException(nameof(findTextBox));
        _languageModeMenu = languageModeMenu ?? throw new ArgumentNullException(nameof(languageModeMenu));
        _openRecentMenu = openRecentMenu ?? throw new ArgumentNullException(nameof(openRecentMenu));
        _windowMenu = windowMenu ?? throw new ArgumentNullException(nameof(windowMenu));
        _toolsMenu = toolsMenu ?? throw new ArgumentNullException(nameof(toolsMenu));
        _statusTabDisplaySizeMenu = statusTabDisplaySizeMenu
            ?? throw new ArgumentNullException(nameof(statusTabDisplaySizeMenu));
        _statusIndentSizeMenu = statusIndentSizeMenu
            ?? throw new ArgumentNullException(nameof(statusIndentSizeMenu));
        _statusFilePathMenu = statusFilePathMenu
            ?? throw new ArgumentNullException(nameof(statusFilePathMenu));
        _indentationStatus = indentationStatus ?? throw new ArgumentNullException(nameof(indentationStatus));
        _filePathStatus = filePathStatus ?? throw new ArgumentNullException(nameof(filePathStatus));

        _windowHandle = WindowNative.GetWindowHandle(window);
        var windowId = Win32Interop.GetWindowIdFromWindow(_windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
    }

    public IntPtr WindowHandle => _windowHandle;

    public AppWindow? AppWindow => _appWindow;

    public WindowLayoutState? WindowSize => _appWindow is { } appWindow
        ? new WindowLayoutState
        {
            Width = appWindow.Size.Width,
            Height = appWindow.Size.Height
        }
        : null;

    public bool IsAlwaysOnTop =>
        _appWindow?.Presenter is OverlappedPresenter presenter
            && presenter.IsAlwaysOnTop;

    public int TabDisplaySize => _editor.TabDisplaySize;

    public int? IndentSize => _editor.IndentSize;

    public IndentationInputMode IndentationInputMode => _editor.IndentationInputMode;

    public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue =>
        _rootGrid.DispatcherQueue;

    public XamlRoot? XamlRoot => _rootGrid.XamlRoot;

    public void ConfigureTheme()
    {
        if (!_themeConfigured)
        {
            _rootGrid.ActualThemeChanged += OnActualThemeChanged;
            _rootGrid.Loaded += OnRootGridLoaded;
            _themeConfigured = true;
        }

        ApplyTheme();
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args) => ApplyTheme();

    private void OnRootGridLoaded(object sender, RoutedEventArgs args) => ApplyTheme();

    private void ApplyTheme() =>
        _editor.ColorScheme = AzunoteSystemColorScheme.Create(_rootGrid.ActualTheme);

    public bool IsFindBoxFocused => _findTextBox.FocusState != FocusState.Unfocused;

    public string Text => _editorBuffer.Text;

    public string SelectedText => _editorBuffer.SelectedText;

    public TextSnapshot Snapshot => _editorBuffer.Snapshot;

    public TextSelection Selection => _editorBuffer.Selection;

    public int CaretPosition => _editorBuffer.CaretPosition;

    public void SetText(string text) => _editorBuffer.SetText(text);

    public void SetSelection(TextSelection selection) => _editorBuffer.SetSelection(selection);

    public void Replace(TextRange range, string replacement) => _editorBuffer.Replace(range, replacement);

    public void Focus() => _editor.Focus(FocusState.Programmatic);

    public void Undo() => _editor.UndoDocument();

    public void Redo() => _editor.RedoDocument();

    public void Cut() => _editor.CutSelectionToClipboard();

    public void Copy() => _editor.CopySelectionToClipboard();

    public void Paste() => _editor.PasteFromClipboard();

    public void SelectAll() => _editor.SelectAll();

    public void MoveToMatchingBracket() => _editor.MoveToMatchingBracket();

    public void Select(TextRange range) => _editor.Select(range.Start, range.Length);

    public void RequestCompletion() => _editor.RequestCompletion();

    public void ShowCompletion(CompletionResult completions) => _editor.ShowCompletion(completions);

    public void ApplyLanguage(EditorLanguageConfiguration configuration)
    {
        _editor.CompletionTriggerCharacters = configuration.CompletionTriggers;
        _editor.Providers.Syntax = configuration.Syntax;
        _editor.Providers.Completion = configuration.Completion;
        _editor.Providers.Tooltip = null;
        _editor.Providers.Folding = null;
        _editor.Providers.Decorations = null;
        _editor.Providers.Gutter = null;
        _editor.Providers.Inlay = null;
        _editor.Providers.BlockAdornment = null;
    }

    public void SetFontFamily(string fontFamily) => _editor.SetFontFamily(fontFamily);

    public void SetFontSize(double fontSize) => _editor.SetFontSize(fontSize);

    internal void SetDiagnosticLogging(IReadOnlyList<string> logging)
    {
        ArgumentNullException.ThrowIfNull(logging);
        var categories = AzunyanDiagnosticCategory.None;
        foreach (var value in logging)
        {
            if (!AzunyanDiagnosticCategories.TryParse(value, out var category))
            {
                throw new ArgumentException(
                    $"Unknown editor diagnostic category '{value}'.",
                    nameof(logging));
            }

            categories |= category;
        }

        _editor.DiagnosticCategories = categories;
    }

    public void RefreshProviders() => _editor.RefreshProviders();

    public void SetWordWrap(bool enabled)
    {
        _editor.TextWrapping = enabled
            ? TextWrapping.Wrap
            : TextWrapping.NoWrap;
    }

    public void SetTabDisplaySize(int size) => _editor.TabDisplaySize = size;

    public void SetIndentSize(int? size) => _editor.IndentSize = size;

    public void SetIndentationInputMode(IndentationInputMode mode) =>
        _editor.IndentationInputMode = mode;

    public void ApplyEditorConfig(EditorConfigSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _editor.TabDisplaySize = settings.GetEffectiveTabWidth();
        _editor.IndentSize = settings.IndentSize;
        _editor.IndentationInputMode =
            settings.IndentationInputMode ?? IndentationInputMode.Auto;
        _editor.SetPreferredLineEnding(settings.LineEnding switch
        {
            LineEndingKind.Lf => "\n",
            LineEndingKind.CrLf => "\r\n",
            LineEndingKind.Cr => "\r",
            _ => null
        });
    }

    public void SetPosition(LineColumn position)
    {
        var snapshot = _editor.Snapshot;
        var zeroBasedLine = Math.Clamp(
            position.Line,
            0,
            Math.Max(0, snapshot.Lines.LineCount - 1));
        var zeroBasedColumn = Math.Min(
            position.Column,
            snapshot.Lines.GetLineLength(zeroBasedLine));
        var absolutePosition = snapshot.Lines.GetPosition(
            new LineColumn(zeroBasedLine, zeroBasedColumn));
        _editor.SetDocumentSelection(TextSelection.Caret(absolutePosition));
        _editor.ScrollSelectionIntoView();
    }

    public void SetStartupPosition(int? line, int? column)
    {
        if (line is null && column is null)
        {
            return;
        }

        var zeroBasedLine = line is > 1 ? line.Value - 1 : 0;
        var zeroBasedColumn = column is > 1 ? column.Value - 1 : 0;
        SetPosition(new LineColumn(zeroBasedLine, zeroBasedColumn));
    }

    public void ToggleAlwaysOnTop()
    {
        if (_appWindow?.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = !presenter.IsAlwaysOnTop;
        }
    }

    public void Minimize()
    {
        if (_appWindow?.Presenter is OverlappedPresenter presenter)
        {
            presenter.Minimize();
        }
    }

    public void RestoreIfMinimized()
    {
        if (_appWindow?.Presenter is OverlappedPresenter presenter
            && presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }
    }

    public void RenderWindowMenu(
        IReadOnlyList<WindowMenuEntry> entries,
        bool isAlwaysOnTop,
        Action<string> onSelected)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(onSelected);

        _viewModel.SetWindowItems(entries, onSelected);
        ClearWindowMenuItems();
        _viewModel.IsAlwaysOnTop = isAlwaysOnTop;
        foreach (var entry in _viewModel.WindowItems)
        {
            var menuItem = new RadioMenuFlyoutItem
            {
                Text = entry.Text,
                GroupName = _radioGroups.OpenWindows,
                IsChecked = entry.IsCurrent,
                Tag = entry.Id,
                Command = entry.SelectCommand
            };
            _windowMenu.Items.Add(menuItem);
            _windowMenuItems.Add(menuItem);
        }
    }

    public void ShowIndentationSizeMenu()
    {
        var settings = TextEditorCommands.GetDocumentIndentationSettings(
            _editor.Snapshot,
            _editor.IndentSize,
            _editor.TabDisplaySize,
            _editor.IndentationInputMode);
        var menu = settings.Kind == IndentationKind.Tabs
            ? _statusTabDisplaySizeMenu
            : _statusIndentSizeMenu;
        menu.ShowAt(_indentationStatus);
    }

    public void ApplyWindowSize(WindowLayoutState size)
    {
        ArgumentNullException.ThrowIfNull(size);
        if (_appWindow is not { } appWindow)
        {
            return;
        }

        var normalized = AzunoteStateNormalization.Normalize(
            new AzunoteState { Window = size }).Window;
        appWindow.Resize(new SizeInt32(normalized.Width, normalized.Height));
    }

    public void RenderRecentFiles(
        IReadOnlyList<string> paths,
        Func<string, Task> onSelected,
        Action<string> onCopyFilePath,
        Func<string, Task> onShowInExplorer,
        Action<string> onRemoved)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(onSelected);
        ArgumentNullException.ThrowIfNull(onCopyFilePath);
        ArgumentNullException.ThrowIfNull(onShowInExplorer);
        ArgumentNullException.ThrowIfNull(onRemoved);

        _viewModel.SetRecentFileItems(
            paths, onSelected, onCopyFilePath, onShowInExplorer, onRemoved);
        ClearRecentFileMenuItems();
        if (_viewModel.RecentFileItems.Count == 0)
        {
            var emptyItem = new MenuFlyoutItem
            {
                Text = "No Recent Files",
                IsEnabled = false
            };
            _openRecentMenu.Items.Add(emptyItem);
            _recentFileMenuItems.Add(emptyItem);
            return;
        }

        foreach (var entry in _viewModel.RecentFileItems)
        {
            var menuItem = new SplitMenuFlyoutItem
            {
                Text = entry.Text,
                Tag = entry.Path,
                Command = entry.OpenCommand,
                Style = _auxiliarySplitMenuFlyoutItemStyle
            };
            AutomationProperties.SetHelpText(menuItem, entry.Path);
            ToolTipService.SetToolTip(menuItem, entry.Path);
            var copyFilePathItem = new MenuFlyoutItem
            {
                Text = "Copy File Path",
                Command = entry.CopyFilePathCommand
            };
            var showInExplorerItem = new MenuFlyoutItem
            {
                Text = "Show in Explorer",
                Command = entry.ShowInExplorerCommand
            };
            var removeItem = new MenuFlyoutItem
            {
                Text = "Remove from list",
                Command = entry.RemoveCommand
            };
            menuItem.Items.Add(copyFilePathItem);
            menuItem.Items.Add(showInExplorerItem);
            menuItem.Items.Add(new MenuFlyoutSeparator());
            menuItem.Items.Add(removeItem);
            _openRecentMenu.Items.Add(menuItem);
            _recentFileMenuItems.Add(menuItem);
        }
    }

    public void ShowFilePathMenu() => _statusFilePathMenu.ShowAt(_filePathStatus);

    public void FocusFind() => _findTextBox.Focus(FocusState.Programmatic);

    public void FocusEditor() => Focus();

    public void SelectFindText() => _findTextBox.SelectAll();

    public void Render(
        IReadOnlyList<LanguageModeEntry> entries,
        int customModeStartIndex,
        Action<string> onSelected,
        Func<string, Task> onEditDefinition,
        Func<string, Task> onShowInExplorer)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(onSelected);
        ArgumentNullException.ThrowIfNull(onEditDefinition);
        ArgumentNullException.ThrowIfNull(onShowInExplorer);
        _viewModel.SetLanguageModeItems(
            entries, onSelected, onEditDefinition, onShowInExplorer);
        _languageModeMenu.Items.Clear();
        _languageModeItems.Clear();
        for (var index = 0; index < entries.Count; index++)
        {
            if (index == 1 || index == customModeStartIndex)
            {
                _languageModeMenu.Items.Add(new MenuFlyoutSeparator());
            }

            var mode = _viewModel.LanguageModeItems[index];
            MenuFlyoutItemBase item;
            if (mode.Entry.DefinitionPath is { } definitionPath)
            {
                var splitItem = new SplitMenuFlyoutItem
                {
                    Text = mode.Entry.DisplayName,
                    Tag = mode.Entry.Id,
                    Command = mode.SelectCommand,
                    Style = _auxiliarySplitMenuFlyoutItemStyle
                };
                AddDefinitionActions(splitItem, definitionPath,
                    mode.EditDefinitionCommand!, mode.ShowInExplorerCommand!);
                item = splitItem;
            }
            else
            {
                var radioItem = new RadioMenuFlyoutItem
                {
                    Text = mode.Entry.DisplayName,
                    GroupName = _radioGroups.LanguageModes,
                    Tag = mode.Entry.Id,
                    Command = mode.SelectCommand
                };
                _languageModeItems[mode.Entry.Id] = radioItem;
                item = radioItem;
            }

            _languageModeMenu.Items.Add(item);
        }
    }

    public void Select(string id)
    {
        foreach (var pair in _languageModeItems)
        {
            pair.Value.IsChecked = string.Equals(
                pair.Key,
                id,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Render(
        IReadOnlyList<ExternalToolMenuNode> nodes,
        Func<ExternalToolSettings, ExternalToolMenuState> getState,
        Func<ExternalToolSettings, Task> onSelected,
        Func<string, Task> onEditDefinition,
        Func<string, Task> onShowInExplorer)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(getState);
        ArgumentNullException.ThrowIfNull(onSelected);
        ArgumentNullException.ThrowIfNull(onEditDefinition);
        ArgumentNullException.ThrowIfNull(onShowInExplorer);
        ClearExternalToolAccelerators();
        ClearExternalToolMenuItems();
        ClearExternalToolContextMenuItems();
        _viewModel.SetExternalToolItems(
            nodes, getState, onSelected, onEditDefinition, onShowInExplorer);
        if (_viewModel.ExternalToolItems.Count > 0)
        {
            var menuItems = CreateExternalToolMenuItems(_viewModel.ExternalToolItems);
            for (var index = 0; index < menuItems.Count; index++)
            {
                _toolsMenu.Items.Insert(index, menuItems[index]);
                _externalToolMenuItems.Add(menuItems[index]);
            }
        }

        var contextMenuItems = CreateExternalToolContextMenuItems(
            _viewModel.ExternalToolContextItems);
        _editor.SetAdditionalContextMenuItems(contextMenuItems);
        _externalToolContextMenuItems.AddRange(contextMenuItems);

        RegisterExternalToolAccelerators(nodes, getState, onSelected);
    }

    public void DisposeEditor()
    {
        if (_themeConfigured)
        {
            _rootGrid.ActualThemeChanged -= OnActualThemeChanged;
            _rootGrid.Loaded -= OnRootGridLoaded;
            _themeConfigured = false;
        }

        _editor.Dispose();
    }

    private void ClearExternalToolAccelerators()
    {
        foreach (var accelerator in _externalToolAccelerators)
        {
            _rootGrid.KeyboardAccelerators.Remove(accelerator);
        }

        _externalToolAccelerators.Clear();
    }

    private void ClearExternalToolMenuItems()
    {
        foreach (var item in _externalToolMenuItems)
        {
            _toolsMenu.Items.Remove(item);
        }

        _externalToolMenuItems.Clear();
    }

    private void ClearExternalToolContextMenuItems()
    {
        _editor.SetAdditionalContextMenuItems([]);
        _externalToolContextMenuItems.Clear();
    }

    private void RegisterExternalToolAccelerators(
        IReadOnlyList<ExternalToolMenuNode> nodes,
        Func<ExternalToolSettings, ExternalToolMenuState> getState,
        Func<ExternalToolSettings, Task> onSelected)
    {
        foreach (var tool in ExternalToolMenuBuilder.EnumerateTools(nodes))
        {
            if (!ExternalToolShortcut.TryParse(tool.Shortcut, out var shortcut)
                || !getState(tool).IsEnabled)
            {
                continue;
            }

            var accelerator = new KeyboardAccelerator
            {
                Key = shortcut!.Key,
                Modifiers = shortcut.Modifiers
            };
            accelerator.Invoked += (sender, args) =>
            {
                args.Handled = true;
                _ = onSelected(tool);
            };
            _rootGrid.KeyboardAccelerators.Add(accelerator);
            _externalToolAccelerators.Add(accelerator);
        }
    }

    private void ClearRecentFileMenuItems()
    {
        foreach (var item in _recentFileMenuItems)
        {
            _openRecentMenu.Items.Remove(item);
        }

        _recentFileMenuItems.Clear();
    }

    private void ClearWindowMenuItems()
    {
        foreach (var item in _windowMenuItems)
        {
            _windowMenu.Items.Remove(item);
        }

        _windowMenuItems.Clear();
    }

    private static IEnumerable<RadioMenuFlyoutItem> GetRadioMenuItems(MenuFlyout menu)
    {
        foreach (var item in menu.Items)
        {
            if (item is RadioMenuFlyoutItem radioItem)
            {
                yield return radioItem;
            }
        }
    }

    private List<MenuFlyoutItemBase> CreateExternalToolMenuItems(
        IReadOnlyList<ExternalToolMenuItemViewModel> nodes)
    {
        var items = new List<MenuFlyoutItemBase>(nodes.Count);
        foreach (var node in nodes)
        {
            if (node.Tool is { } tool)
            {
                MenuFlyoutItemBase menuItem;
                if (tool.DefinitionPath is { } definitionPath)
                {
                    var splitItem = new SplitMenuFlyoutItem
                    {
                        Text = node.Text,
                        IsEnabled = node.State!.IsEnabled,
                        Command = node.RunCommand,
                        Style = _auxiliarySplitMenuFlyoutItemStyle
                    };
                    AddDefinitionActions(splitItem, definitionPath,
                        node.EditDefinitionCommand!, node.ShowInExplorerCommand!);
                    menuItem = splitItem;
                }
                else
                {
                    var regularItem = new MenuFlyoutItem
                    {
                        Text = node.Text,
                        IsEnabled = node.State!.IsEnabled,
                        Command = node.RunCommand
                    };
                    menuItem = regularItem;
                }

                AutomationProperties.SetHelpText(
                    menuItem,
                    node.State.DisabledReason ?? string.Empty);
                if (node.State.DisabledReason is { } reason)
                {
                    ToolTipService.SetToolTip(menuItem, reason);
                }
                if (ExternalToolShortcut.TryParse(tool.Shortcut, out _))
                {
                    if (menuItem is MenuFlyoutItem regularItem)
                    {
                        regularItem.KeyboardAcceleratorTextOverride = tool.Shortcut;
                    }
                }

                items.Add(menuItem);
                continue;
            }

            var subMenu = new MenuFlyoutSubItem { Text = node.Text };
            foreach (var child in CreateExternalToolMenuItems(node.Children))
            {
                subMenu.Items.Add(child);
            }

            items.Add(subMenu);
        }

        return items;
    }

    private static List<MenuFlyoutItemBase> CreateExternalToolContextMenuItems(
        IReadOnlyList<ExternalToolMenuItemViewModel> entries)
    {
        var items = new List<MenuFlyoutItemBase>(entries.Count);
        foreach (var entry in entries)
        {
            var item = new MenuFlyoutItem
            {
                Text = entry.Text,
                IsEnabled = entry.State!.IsEnabled,
                Command = entry.RunCommand
            };
            AutomationProperties.SetHelpText(
                item,
                entry.State.DisabledReason ?? string.Empty);
            if (entry.State.DisabledReason is { } reason)
            {
                ToolTipService.SetToolTip(item, reason);
            }

            items.Add(item);
        }

        return items;
    }

    private static void AddDefinitionActions(
        SplitMenuFlyoutItem menuItem,
        string definitionPath,
        System.Windows.Input.ICommand editDefinitionCommand,
        System.Windows.Input.ICommand showInExplorerCommand)
    {
        AutomationProperties.SetHelpText(menuItem, definitionPath);
        ToolTipService.SetToolTip(menuItem, definitionPath);

        var editItem = new MenuFlyoutItem
        {
            Text = "Edit...",
            Command = editDefinitionCommand
        };
        var showInExplorerItem = new MenuFlyoutItem
        {
            Text = "Show in Explorer",
            Command = showInExplorerCommand
        };
        menuItem.Items.Add(editItem);
        menuItem.Items.Add(showInExplorerItem);
    }

}
