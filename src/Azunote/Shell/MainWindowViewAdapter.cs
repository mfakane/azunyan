using Azunyan.Core;
using Azunyan.WinUI;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using WinRT.Interop;

namespace Azunote;

internal sealed class MainWindowViewAdapter :
    IEditorView,
    IStatusBarView,
    IWindowChromeView,
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
    private readonly MenuFlyoutSubItem _externalToolsMenu;
    private readonly MenuFlyoutItem _wordWrapMenuItem;
    private readonly Border _statusBarPanel;
    private readonly TextBlock _positionStatus;
    private readonly TextBlock _encodingStatus;
    private readonly TextBlock _lineEndingStatus;
    private readonly TextBlock _indentationStatus;
    private readonly TextBlock _filePathStatus;
    private readonly Dictionary<string, ToggleMenuFlyoutItem> _languageModeItems =
        new(StringComparer.OrdinalIgnoreCase);

    public MainWindowViewAdapter(
        MainWindow window,
        AzunyanEditorView editor,
        Grid rootGrid,
        Border findPanel,
        TextBox findTextBox,
        TextBox replaceTextBox,
        TextBlock findResultText,
        MenuFlyoutSubItem languageModeMenu,
        MenuFlyoutSubItem externalToolsMenu,
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
        _externalToolsMenu = externalToolsMenu ?? throw new ArgumentNullException(nameof(externalToolsMenu));
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

    public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue =>
        _rootGrid.DispatcherQueue;

    public XamlRoot? XamlRoot => _rootGrid.XamlRoot;

    public void ConfigureTheme() =>
        _editor.ColorScheme = AzunoteSystemColorScheme.CreateLight();

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
        Func<ExternalToolSettings, Task> onSelected)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(onSelected);
        _externalToolsMenu.Items.Clear();
        if (nodes.Count == 0)
        {
            _externalToolsMenu.Items.Add(new MenuFlyoutItem
            {
                Text = "No tools configured",
                IsEnabled = false
            });
            return;
        }

        AddExternalToolMenuItems(_externalToolsMenu, nodes, onSelected);
    }

    public void DisposeEditor() => _editor.Dispose();

    private static void AddExternalToolMenuItems(
        MenuFlyoutSubItem parent,
        IReadOnlyList<ExternalToolMenuNode> nodes,
        Func<ExternalToolSettings, Task> onSelected)
    {
        foreach (var node in nodes)
        {
            if (node.Tool is { } tool)
            {
                var menuItem = new MenuFlyoutItem { Text = node.Name };
                menuItem.Click += (_, _) => _ = onSelected(tool);
                parent.Items.Add(menuItem);
                continue;
            }

            var subMenu = new MenuFlyoutSubItem { Text = node.Name };
            AddExternalToolMenuItems(subMenu, node.Children, onSelected);
            if (subMenu.Items.Count > 0)
            {
                parent.Items.Add(subMenu);
            }
        }
    }
}
