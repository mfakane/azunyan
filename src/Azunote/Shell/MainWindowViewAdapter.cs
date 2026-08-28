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
    IStatusBarView,
    IWindowChromeView,
    IWindowMenuView,
    IFindReplaceView,
    ILanguageModeMenuView,
    IExternalToolMenuView
{
    private readonly AzunyanEditorView _editor;
    private readonly AzunyanEditorBuffer _editorBuffer;
    private readonly IntPtr _windowHandle;
    private readonly AppWindow? _appWindow;
    private readonly Grid _rootGrid;
    private readonly Border _findPanel;
    private readonly TextBox _findTextBox;
    private readonly TextBox _replaceTextBox;
    private readonly TextBlock _findResultText;
    private readonly MenuFlyoutSubItem _languageModeMenu;
    private readonly MenuFlyoutSubItem _openRecentMenu;
    private readonly MenuBarItem _windowMenu;
    private readonly ToggleMenuFlyoutItem _alwaysOnTopMenuItem;
    private readonly MenuBarItem _toolsMenu;
    private readonly MenuFlyoutItem _wordWrapMenuItem;
    private readonly IReadOnlyDictionary<int, ToggleMenuFlyoutItem> _tabDisplaySizeMenuItems;
    private readonly MenuFlyout _statusTabDisplaySizeMenu;
    private readonly IReadOnlyList<(int? Size, ToggleMenuFlyoutItem Item)> _indentSizeMenuItems;
    private readonly MenuFlyout _statusIndentSizeMenu;
    private readonly IReadOnlyList<(IndentationInputMode Mode, ToggleMenuFlyoutItem Item)> _indentationInputModeMenuItems;
    private readonly MenuFlyout _statusFilePathMenu;
    private readonly Border _statusBarPanel;
    private readonly TextBlock _positionStatus;
    private readonly TextBlock _encodingStatus;
    private readonly TextBlock _lineEndingStatus;
    private readonly TextBlock _indentationStatus;
    private readonly TextBlock _filePathStatus;
    private readonly List<KeyboardAccelerator> _externalToolAccelerators = [];
    private readonly List<MenuFlyoutItemBase> _externalToolMenuItems = [];
    private readonly List<MenuFlyoutItemBase> _recentFileMenuItems = [];
    private readonly List<MenuFlyoutItemBase> _windowMenuItems = [];
    private readonly Dictionary<string, ToggleMenuFlyoutItem> _languageModeItems =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _themeConfigured;

    public MainWindowViewAdapter(
        MainWindow window,
        AzunyanEditorView editor,
        Grid rootGrid,
        Border findPanel,
        TextBox findTextBox,
        TextBox replaceTextBox,
        TextBlock findResultText,
        MenuFlyoutSubItem languageModeMenu,
        MenuFlyoutSubItem openRecentMenu,
        MenuBarItem windowMenu,
        ToggleMenuFlyoutItem alwaysOnTopMenuItem,
        MenuBarItem toolsMenu,
        MenuFlyoutItem wordWrapMenuItem,
        ToggleMenuFlyoutItem tabDisplaySize2MenuItem,
        ToggleMenuFlyoutItem tabDisplaySize4MenuItem,
        ToggleMenuFlyoutItem tabDisplaySize8MenuItem,
        MenuFlyout statusTabDisplaySizeMenu,
        ToggleMenuFlyoutItem indentSizeAutoMenuItem,
        ToggleMenuFlyoutItem indentSize2MenuItem,
        ToggleMenuFlyoutItem indentSize4MenuItem,
        ToggleMenuFlyoutItem indentSize8MenuItem,
        MenuFlyout statusIndentSizeMenu,
        ToggleMenuFlyoutItem indentationInputModeAutoMenuItem,
        ToggleMenuFlyoutItem indentationInputModeTabMenuItem,
        ToggleMenuFlyoutItem indentationInputModeSpacesMenuItem,
        MenuFlyout statusFilePathMenu,
        Border statusBarPanel,
        TextBlock positionStatus,
        TextBlock encodingStatus,
        TextBlock lineEndingStatus,
        TextBlock indentationStatus,
        TextBlock filePathStatus)
    {
        ArgumentNullException.ThrowIfNull(window);
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _editorBuffer = new AzunyanEditorBuffer(editor);
        _rootGrid = rootGrid ?? throw new ArgumentNullException(nameof(rootGrid));
        _findPanel = findPanel ?? throw new ArgumentNullException(nameof(findPanel));
        _findTextBox = findTextBox ?? throw new ArgumentNullException(nameof(findTextBox));
        _replaceTextBox = replaceTextBox ?? throw new ArgumentNullException(nameof(replaceTextBox));
        _findResultText = findResultText ?? throw new ArgumentNullException(nameof(findResultText));
        _languageModeMenu = languageModeMenu ?? throw new ArgumentNullException(nameof(languageModeMenu));
        _openRecentMenu = openRecentMenu ?? throw new ArgumentNullException(nameof(openRecentMenu));
        _windowMenu = windowMenu ?? throw new ArgumentNullException(nameof(windowMenu));
        _alwaysOnTopMenuItem = alwaysOnTopMenuItem ?? throw new ArgumentNullException(nameof(alwaysOnTopMenuItem));
        _toolsMenu = toolsMenu ?? throw new ArgumentNullException(nameof(toolsMenu));
        _wordWrapMenuItem = wordWrapMenuItem ?? throw new ArgumentNullException(nameof(wordWrapMenuItem));
        _tabDisplaySizeMenuItems = new Dictionary<int, ToggleMenuFlyoutItem>
        {
            [2] = tabDisplaySize2MenuItem ?? throw new ArgumentNullException(nameof(tabDisplaySize2MenuItem)),
            [4] = tabDisplaySize4MenuItem ?? throw new ArgumentNullException(nameof(tabDisplaySize4MenuItem)),
            [8] = tabDisplaySize8MenuItem ?? throw new ArgumentNullException(nameof(tabDisplaySize8MenuItem))
        };
        _statusTabDisplaySizeMenu = statusTabDisplaySizeMenu
            ?? throw new ArgumentNullException(nameof(statusTabDisplaySizeMenu));
        _indentSizeMenuItems =
        [
            (null, indentSizeAutoMenuItem ?? throw new ArgumentNullException(nameof(indentSizeAutoMenuItem))),
            (2, indentSize2MenuItem ?? throw new ArgumentNullException(nameof(indentSize2MenuItem))),
            (4, indentSize4MenuItem ?? throw new ArgumentNullException(nameof(indentSize4MenuItem))),
            (8, indentSize8MenuItem ?? throw new ArgumentNullException(nameof(indentSize8MenuItem)))
        ];
        _statusIndentSizeMenu = statusIndentSizeMenu
            ?? throw new ArgumentNullException(nameof(statusIndentSizeMenu));
        _indentationInputModeMenuItems =
        [
            (IndentationInputMode.Auto, indentationInputModeAutoMenuItem ?? throw new ArgumentNullException(nameof(indentationInputModeAutoMenuItem))),
            (IndentationInputMode.Tab, indentationInputModeTabMenuItem ?? throw new ArgumentNullException(nameof(indentationInputModeTabMenuItem))),
            (IndentationInputMode.Spaces, indentationInputModeSpacesMenuItem ?? throw new ArgumentNullException(nameof(indentationInputModeSpacesMenuItem)))
        ];
        _statusFilePathMenu = statusFilePathMenu
            ?? throw new ArgumentNullException(nameof(statusFilePathMenu));
        _statusBarPanel = statusBarPanel ?? throw new ArgumentNullException(nameof(statusBarPanel));
        _positionStatus = positionStatus ?? throw new ArgumentNullException(nameof(positionStatus));
        _encodingStatus = encodingStatus ?? throw new ArgumentNullException(nameof(encodingStatus));
        _lineEndingStatus = lineEndingStatus ?? throw new ArgumentNullException(nameof(lineEndingStatus));
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

    public bool IsStatusBarVisible => _statusBarPanel.Visibility == Visibility.Visible;

    public bool IsVisible => _findPanel.Visibility == Visibility.Visible;

    public bool IsFindBoxFocused => _findTextBox.FocusState != FocusState.Unfocused;

    public string FindText
    {
        get => _findTextBox.Text;
        set => _findTextBox.Text = value;
    }

    public string ReplaceText
    {
        get => _replaceTextBox.Text;
        set => _replaceTextBox.Text = value;
    }

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

    public void Select(TextRange range) => _editor.Select(range.Start, range.Length);

    public void RequestCompletion() => _editor.RequestCompletion();

    public void ApplyLanguage(EditorLanguageConfiguration configuration)
    {
        _editor.CompletionTriggerCharacters = configuration.CompletionTriggers;
        _editor.Providers.Syntax = configuration.Syntax;
        _editor.Providers.Completion = configuration.Completion;
        _editor.Providers.Tooltip = null;
        _editor.Providers.Folding = null;
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

    public void SetStartupPosition(int? line, int? column)
    {
        if (line is null && column is null)
        {
            return;
        }

        var snapshot = _editor.Snapshot;
        var zeroBasedLine = Math.Clamp((line ?? 1) - 1, 0, snapshot.Lines.LineCount - 1);
        var zeroBasedColumn = Math.Max(0, (column ?? 1) - 1);
        var clampedColumn = Math.Min(zeroBasedColumn, snapshot.Lines.GetLineLength(zeroBasedLine));
        _editor.SetDocumentSelection(TextSelection.Caret(
            snapshot.Lines.GetPosition(new LineColumn(zeroBasedLine, clampedColumn))));
    }

    public void Apply(StatusBarState state)
    {
        _positionStatus.Text = state.Position;
        _encodingStatus.Text = state.Encoding;
        _lineEndingStatus.Text = state.LineEnding;
        _indentationStatus.Text = state.Indentation;
        _filePathStatus.Text = state.FilePath;
        SetFilePathMenuState(state.HasFilePath);
    }

    public void SetTitle(string title)
    {
        if (_appWindow is not null)
        {
            _appWindow.Title = title;
        }
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

        ClearWindowMenuItems();
        _alwaysOnTopMenuItem.IsChecked = isAlwaysOnTop;
        foreach (var entry in entries)
        {
            var menuItem = new ToggleMenuFlyoutItem
            {
                Text = entry.DocumentName,
                IsChecked = entry.IsCurrent,
                Tag = entry.Id
            };
            menuItem.Click += (_, _) => onSelected(entry.Id);
            _windowMenu.Items.Add(menuItem);
            _windowMenuItems.Add(menuItem);
        }
    }

    public void SetStatusBarVisible(bool visible) =>
        _statusBarPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public void SetWordWrapLabel(bool enabled) =>
        _wordWrapMenuItem.Text = enabled ? "Word Wrap ✓" : "Word Wrap";

    public void SetTabDisplaySizeLabel(int size)
    {
        foreach (var item in _tabDisplaySizeMenuItems)
        {
            item.Value.IsChecked = item.Key == size;
        }

        foreach (var item in GetToggleMenuItems(_statusTabDisplaySizeMenu))
        {
            item.IsChecked = item.Tag is string tag
                && int.TryParse(tag, out var itemSize)
                && itemSize == size;
        }
    }

    public void SetIndentSizeLabel(int? size)
    {
        foreach (var item in _indentSizeMenuItems)
        {
            item.Item.IsChecked = item.Size == size;
        }

        foreach (var item in GetToggleMenuItems(_statusIndentSizeMenu))
        {
            item.IsChecked = item.Tag is string tag
                && (string.Equals(tag, "auto", StringComparison.Ordinal)
                    ? size is null
                    : int.TryParse(tag, out var itemSize) && itemSize == size);
        }
    }

    public void SetIndentationInputModeLabel(IndentationInputMode mode)
    {
        foreach (var item in _indentationInputModeMenuItems)
        {
            item.Item.IsChecked = item.Mode == mode;
        }
    }

    public void ShowIndentationSizeMenu()
    {
        var settings = TextEditorCommands.GetIndentationSettings(
            _editor.Snapshot,
            _editorBuffer.CaretPosition,
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
        Func<string, Task> onSelected)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(onSelected);

        ClearRecentFileMenuItems();
        if (paths.Count == 0)
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

        foreach (var path in paths)
        {
            var menuItem = new MenuFlyoutItem
            {
                Text = GetRecentFileDisplayName(path),
                Tag = path
            };
            AutomationProperties.SetHelpText(menuItem, path);
            ToolTipService.SetToolTip(menuItem, path);
            menuItem.Click += (_, _) => _ = onSelected(path);
            _openRecentMenu.Items.Add(menuItem);
            _recentFileMenuItems.Add(menuItem);
        }
    }

    public void ShowFilePathMenu() => _statusFilePathMenu.ShowAt(_filePathStatus);

    public void Show(bool replace)
    {
        _findPanel.Visibility = Visibility.Visible;
        FocusFind();
        if (_editorBuffer.Selection.Length > 0 && !string.IsNullOrEmpty(_editor.SelectedText))
        {
            _findTextBox.Text = _editor.SelectedText;
            _findTextBox.SelectAll();
        }

        if (!replace)
        {
            _replaceTextBox.Text = string.Empty;
        }
    }

    public void Close()
    {
        _findPanel.Visibility = Visibility.Collapsed;
        FocusEditor();
    }

    public void FocusFind() => _findTextBox.Focus(FocusState.Programmatic);

    public void FocusEditor() => Focus();

    public void SetResult(string message) => _findResultText.Text = message;

    public void Render(
        IReadOnlyList<LanguageModeEntry> entries,
        int customModeStartIndex,
        Action<string> onSelected)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(onSelected);
        _languageModeMenu.Items.Clear();
        _languageModeItems.Clear();
        for (var index = 0; index < entries.Count; index++)
        {
            if (index == 1 || index == customModeStartIndex)
            {
                _languageModeMenu.Items.Add(new MenuFlyoutSeparator());
            }

            var mode = entries[index];
            var item = new ToggleMenuFlyoutItem
            {
                Text = mode.DisplayName,
                Tag = mode.Id
            };
            item.Click += (_, _) => onSelected(mode.Id);
            _languageModeItems[mode.Id] = item;
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
        Func<ExternalToolSettings, Task> onSelected)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(getState);
        ArgumentNullException.ThrowIfNull(onSelected);
        ClearExternalToolAccelerators();
        ClearExternalToolMenuItems();
        var visibleEntries = ExternalToolMenuBuilder.Build(nodes, getState);
        if (visibleEntries.Count == 0)
        {
            var emptyItem = new MenuFlyoutItem
            {
                Text = "No tools available",
                IsEnabled = false
            };
            _toolsMenu.Items.Insert(0, emptyItem);
            _externalToolMenuItems.Add(emptyItem);
            return;
        }

        var menuItems = CreateExternalToolMenuItems(visibleEntries, onSelected);
        for (var index = 0; index < menuItems.Count; index++)
        {
            _toolsMenu.Items.Insert(index, menuItems[index]);
            _externalToolMenuItems.Add(menuItems[index]);
        }
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

    private static IEnumerable<ToggleMenuFlyoutItem> GetToggleMenuItems(MenuFlyout menu)
    {
        foreach (var item in menu.Items)
        {
            if (item is ToggleMenuFlyoutItem toggleItem)
            {
                yield return toggleItem;
            }
        }
    }

    private static string GetRecentFileDisplayName(string path)
    {
        var fileName = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(fileName) ? path : fileName;
    }

    private void SetFilePathMenuState(bool hasFilePath)
    {
        foreach (var item in _statusFilePathMenu.Items)
        {
            if (item is MenuFlyoutItem menuItem
                && (string.Equals(menuItem.Tag as string, "show-in-explorer", StringComparison.Ordinal)
                    || string.Equals(menuItem.Tag as string, "open-folder-in-terminal", StringComparison.Ordinal)))
            {
                menuItem.IsEnabled = hasFilePath;
            }
        }
    }

    private List<MenuFlyoutItemBase> CreateExternalToolMenuItems(
        IReadOnlyList<ExternalToolMenuEntry> nodes,
        Func<ExternalToolSettings, Task> onSelected)
    {
        var items = new List<MenuFlyoutItemBase>(nodes.Count);
        foreach (var node in nodes)
        {
            if (node.Tool is { } tool)
            {
                var menuItem = new MenuFlyoutItem
                {
                    Text = node.Name,
                    IsEnabled = node.State!.IsEnabled
                };
                AutomationProperties.SetHelpText(
                    menuItem,
                    node.State.DisabledReason ?? string.Empty);
                if (node.State.DisabledReason is { } reason)
                {
                    ToolTipService.SetToolTip(menuItem, reason);
                }
                menuItem.Click += (_, _) => _ = onSelected(tool);
                if (ExternalToolShortcut.TryParse(tool.Shortcut, out var shortcut))
                {
                    menuItem.KeyboardAcceleratorTextOverride = tool.Shortcut;
                    if (node.State.IsEnabled)
                    {
                        // MenuFlyoutSubItem descendants created at runtime are
                        // only registered for keyboard accelerators while the
                        // submenu is open. Register the shortcut on the
                        // always-loaded root instead; the menu item only keeps
                        // the display text for the same shortcut.
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

                items.Add(menuItem);
                continue;
            }

            var subMenu = new MenuFlyoutSubItem { Text = node.Name };
            foreach (var child in CreateExternalToolMenuItems(node.Children, onSelected))
            {
                subMenu.Items.Add(child);
            }

            items.Add(subMenu);
        }

        return items;
    }

}
