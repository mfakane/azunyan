using Azunyan.Core;
using Azunyan.WinUI;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinRT.Interop;

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
    private readonly MenuBarItem _windowMenu;
    private readonly ToggleMenuFlyoutItem _alwaysOnTopMenuItem;
    private readonly MenuBarItem _toolsMenu;
    private readonly MenuFlyoutItem _wordWrapMenuItem;
    private readonly Border _statusBarPanel;
    private readonly TextBlock _positionStatus;
    private readonly TextBlock _encodingStatus;
    private readonly TextBlock _lineEndingStatus;
    private readonly TextBlock _indentationStatus;
    private readonly TextBlock _filePathStatus;
    private readonly List<KeyboardAccelerator> _externalToolAccelerators = [];
    private readonly List<MenuFlyoutItemBase> _externalToolMenuItems = [];
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
        MenuBarItem windowMenu,
        ToggleMenuFlyoutItem alwaysOnTopMenuItem,
        MenuBarItem toolsMenu,
        MenuFlyoutItem wordWrapMenuItem,
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
        _windowMenu = windowMenu ?? throw new ArgumentNullException(nameof(windowMenu));
        _alwaysOnTopMenuItem = alwaysOnTopMenuItem ?? throw new ArgumentNullException(nameof(alwaysOnTopMenuItem));
        _toolsMenu = toolsMenu ?? throw new ArgumentNullException(nameof(toolsMenu));
        _wordWrapMenuItem = wordWrapMenuItem ?? throw new ArgumentNullException(nameof(wordWrapMenuItem));
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

    public bool IsAlwaysOnTop =>
        _appWindow?.Presenter is OverlappedPresenter presenter
            && presenter.IsAlwaysOnTop;

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

    private void ClearWindowMenuItems()
    {
        foreach (var item in _windowMenuItems)
        {
            _windowMenu.Items.Remove(item);
        }

        _windowMenuItems.Clear();
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
