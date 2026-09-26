using Azunyan.Core;
using Azunyan.WinUI;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinRT;
using WinRT.Interop;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.Graphics;

namespace Azunote;

public sealed partial class MainWindow
{
    internal const int WindowGroupAccentPaletteSize = 6;

    private AzunyanEditorView _editor => Editor;
    private Grid _rootGrid => RootGrid;
    private TextBox _findTextBox => FindTextBox;
    private TextBox _replaceTextBox => ReplaceTextBox;
    private MenuFlyoutSubItem _languageModeMenu => LanguageModeMenuItem;
    private MenuFlyoutSubItem _openRecentMenu => OpenRecentMenuItem;
    private MenuBarItem _windowMenu => WindowMenuItem;
    private MenuBarItem _toolsMenu => ToolsMenuItem;
    private MenuFlyout _statusTabDisplaySizeMenu => StatusTabDisplaySizeMenu;
    private MenuFlyout _statusIndentSizeMenu => StatusIndentSizeMenu;
    private MenuFlyout _statusFilePathMenu => StatusFilePathMenu;
    private TextBlock _indentationStatus => IndentationStatus;
    private TextBlock _filePathStatus => FilePathStatus;
    private MainWindowRadioGroupNames _radioGroups => ViewModel.Groups;
    private MainWindowViewModel _viewModel => ViewModel;
    private Style _auxiliarySplitMenuFlyoutItemStyle => AuxiliarySplitMenuFlyoutItemStyle;

    private AzunyanEditorBuffer _editorBuffer = null!;
    private IntPtr _windowHandle;
    private readonly List<KeyboardAccelerator> _externalToolAccelerators = [];
    private IReadOnlyList<ExternalToolMenuNode>? _renderedToolNodes;
    private Dictionary<ExternalToolSettings, ExternalToolMenuState> _renderedToolStates = [];
    private readonly Dictionary<ExternalToolSettings, List<MenuFlyoutItemBase>> _toolControls = [];
    private readonly List<MenuFlyoutItemBase> _externalToolMenuItems = [];
    private readonly List<MenuFlyoutItemBase> _externalToolContextMenuItems = [];
    private readonly List<MenuFlyoutItemBase> _recentFileMenuItems = [];
    private readonly List<MenuFlyoutItemBase> _windowMenuItems = [];
    private readonly List<MenuFlyoutSeparator> _windowMenuSeparators = [];
    private readonly Dictionary<string, RadioMenuFlyoutItem> _languageModeItems =
        new(StringComparer.OrdinalIgnoreCase);
    private int? _windowGroupAccentIndex;
    private bool _isWindowActive = true;
    private UISettings? _uiSettings;
    private bool _themeConfigured;
    private Flyout _findNotificationFlyout = null!;
    private TextBlock _findNotificationText = null!;

    internal Document Document => _editor.Document;

    private async void Editor_LinkInvoked(object? sender, LinkInvokedEventArgs args)
    {
        try
        {
            await _runtime.OpenLinkAsync(args.Text);
        }
        catch (Exception exception)
        {
            ErrorReporter.LogException($"Open link: {args.Text}", exception);
        }
    }

    internal void SetDocument(Document document) => _editor.SetDocument(document);

    private void InitializeView()
    {
        Editor.DiagnosticSink = (category, message) => ErrorReporter.QueueMessage(
            $"Editor diagnostic/{AzunyanDiagnosticCategories.GetName(category)}",
            message);
        Editor.DiagnosticExceptionSink = (source, exception) => ErrorReporter.LogException(
            $"Editor exception/{source}",
            exception);
        Editor.LinkInvoked += Editor_LinkInvoked;
        _editorBuffer = new AzunyanEditorBuffer(Editor);
        _findNotificationFlyout = FindNotificationFlyout;
        _findNotificationText = _findNotificationFlyout.Content as TextBlock
            ?? throw new InvalidOperationException("Find notification flyout must contain a TextBlock.");

        _windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow?.SetIcon(Path.Combine(ApplicationLocation.BundleDirectory, "Assets", "app.ico"));
        _uiSettings = new UISettings();
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
    }

    internal IntPtr WindowHandle => _windowHandle;

    private WindowLayoutState? WindowSizeView => _appWindow is { } appWindow
        ? new WindowLayoutState
        {
            Width = appWindow.Size.Width,
            Height = appWindow.Size.Height
        }
        : null;

    private bool IsAlwaysOnTopView =>
        GetOverlappedPresenter()?.IsAlwaysOnTop ?? false;

    private int TabDisplaySize => _editor.TabDisplaySize;

    private int? IndentSize => _editor.IndentSize;

    private IndentationInputMode IndentationInputMode => _editor.IndentationInputMode;

    internal XamlRoot? XamlRoot => _rootGrid.XamlRoot;

    private OverlappedPresenter? GetOverlappedPresenter()
    {
        var presenter = _appWindow?.Presenter;
        return presenter is null ? null : presenter.As<OverlappedPresenter>();
    }

    private void ConfigureTheme()
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

    private void OnRootGridLoaded(object sender, RoutedEventArgs args)
    {
        WindowMenuBeepSuppressor.Install(_windowHandle);
        ApplyTheme();
    }

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        if (_rootGrid.DispatcherQueue.HasThreadAccess)
        {
            ApplyWindowGroupAccentCore();
        }
        else
        {
            _rootGrid.DispatcherQueue.TryEnqueue(ApplyWindowGroupAccentCore);
        }
    }

    private void ApplyTheme()
    {
        _editor.ColorScheme = AzunoteSystemColorScheme.Create(_rootGrid.ActualTheme);
        ApplyWindowGroupAccentCore();
    }

    internal void SetWindowGroupAccent(int? accentIndex)
    {
        _windowGroupAccentIndex = accentIndex;
        ApplyWindowGroupAccentCore();
    }

    private void ApplyWindowGroupAccentCore()
    {
        if (_windowGroupAccentIndex is not { } accentIndex
            || new AccessibilitySettings().HighContrast)
        {
            WindowGroupAccentStrip.Visibility = Visibility.Collapsed;
            return;
        }

        var accentColor = _uiSettings?.GetColorValue(UIColorType.Accent)
            ?? new UISettings().GetColorValue(UIColorType.Accent);
        var accent = WindowGroupAccentPalette.Get(
            accentIndex,
            _rootGrid.ActualTheme,
            accentColor);
        WindowGroupAccentStrip.Background = new SolidColorBrush(
            _isWindowActive ? accent.Active : accent.Inactive);
        WindowGroupAccentStrip.Visibility = Visibility.Visible;
    }

    private bool IsFindBoxFocused => _findTextBox.FocusState != FocusState.Unfocused;

    private string Text => _editorBuffer.Text;

    private string SelectedText => _editorBuffer.SelectedText;

    private TextSnapshot Snapshot => _editorBuffer.Snapshot;

    private TextSelection Selection => _editorBuffer.Selection;

    private int CaretPosition => _editorBuffer.CaretPosition;

    private void SetText(string text) => _editorBuffer.SetText(text);

    private void SetSelection(TextSelection selection) => _editorBuffer.SetSelection(selection);

    private void Replace(TextRange range, string replacement) => _editorBuffer.Replace(range, replacement);

    private void Focus() => _editor.Focus(FocusState.Programmatic);

    private void BeginUndoGroup() => _editor.BeginUndoGroup();

    private void EndUndoGroup() => _editor.EndUndoGroup();

    private void Undo() => _editor.UndoDocument();

    private void Redo() => _editor.RedoDocument();

    private void Cut() => _editor.CutSelectionToClipboard();

    private void Copy() => _editor.CopySelectionToClipboard();

    private void Paste() => _editor.PasteFromClipboard();

    private void SelectAll() => _editor.SelectAll();

    private void MoveToMatchingBracket() => _editor.MoveToMatchingBracket();

    private void Select(TextRange range) => _editor.Select(range.Start, range.Length);

    private void RequestCompletion() => _editor.RequestCompletion();

    private void ShowCompletion(CompletionResult completions) => _editor.ShowCompletion(completions);

    private void ApplyLanguage(EditorLanguageConfiguration configuration)
    {
        _editor.CompletionTriggerCharacters = configuration.CompletionTriggers;
        _editor.Providers.Syntax = configuration.Syntax;
        _editor.Providers.Completion = configuration.Completion;
        _editor.Providers.Folding = configuration.Folding;
        _editor.Providers.Tooltip = null;
        _editor.Providers.Decorations = null;
        _editor.Providers.Gutter = null;
        _editor.Providers.Inlay = null;
        _editor.Providers.BlockAdornment = null;
    }

    private void SetFontFamily(string fontFamily) => _editor.SetFontFamily(fontFamily);

    private void SetFontSize(double fontSize) => _editor.SetFontSize(fontSize);

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

    private void RefreshProviders() => _editor.RefreshProviders();

    private void SetWordWrap(bool enabled)
    {
        _editor.TextWrapping = enabled
            ? TextWrapping.Wrap
            : TextWrapping.NoWrap;
    }

    private void SetTabDisplaySize(int size) => _editor.TabDisplaySize = size;

    private void SetIndentSize(int? size) => _editor.IndentSize = size;

    private void SetIndentationInputMode(IndentationInputMode mode) =>
        _editor.IndentationInputMode = mode;

    private void ApplyEditorConfig(EditorConfigSettings settings)
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

    private void SetPosition(LineColumn position)
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

    private void SetStartupPosition(int? line, int? column)
    {
        if (line is null && column is null)
        {
            return;
        }

        var zeroBasedLine = line is > 1 ? line.Value - 1 : 0;
        var zeroBasedColumn = column is > 1 ? column.Value - 1 : 0;
        SetPosition(new LineColumn(zeroBasedLine, zeroBasedColumn));
    }

    internal void ToggleAlwaysOnTop()
    {
        if (GetOverlappedPresenter() is { } presenter)
        {
            presenter.IsAlwaysOnTop = !presenter.IsAlwaysOnTop;
        }
    }

    private void MinimizeView()
    {
        if (GetOverlappedPresenter() is { } presenter)
        {
            presenter.Minimize();
        }
    }

    private void RestoreIfMinimizedView()
    {
        if (GetOverlappedPresenter() is { } presenter
            && presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }
    }

    private void RenderWindowMenuCore(
        IReadOnlyList<WindowMenuEntry> entries,
        bool isAlwaysOnTop,
        Action<string> onSelected)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(onSelected);

        _viewModel.SetWindowItems(entries, onSelected);
        ClearWindowMenuItems();
        _viewModel.IsAlwaysOnTop = isAlwaysOnTop;
        WindowMenuItemViewModel? previousEntry = null;
        foreach (var entry in _viewModel.WindowItems)
        {
            if (entry.IsGroupStart
                && previousEntry is not null
                && (previousEntry.IsDuplicateGroup || entry.IsDuplicateGroup))
            {
                var separator = new MenuFlyoutSeparator();
                _windowMenu.Items.Add(separator);
                _windowMenuSeparators.Add(separator);
            }

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
            previousEntry = entry;
        }
    }

    internal void ShowIndentationSizeMenu()
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

    private void ApplyWindowSizeView(WindowLayoutState size)
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

    internal void RenderRecentFiles(
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

    internal void ShowFilePathMenu() => _statusFilePathMenu.ShowAt(_filePathStatus);

    private void FocusFind() => _findTextBox.Focus(FocusState.Programmatic);

    private void FocusEditorView() => Focus();

    private void SelectFindText() => _findTextBox.SelectAll();

    private void ShowFindNotification(string message, bool replaceMode)
    {
        _findNotificationText.Text = message;
        _findNotificationFlyout.ShowAt(replaceMode ? _replaceTextBox : _findTextBox);
    }

    private void HideFindNotification() => _findNotificationFlyout.Hide();

    private void RenderLanguageModes(
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

    private void SelectLanguageMode(string id)
    {
        foreach (var pair in _languageModeItems)
        {
            pair.Value.IsChecked = string.Equals(
                pair.Key,
                id,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private void RenderExternalTools(
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
        using var measurement = ShellPerformance.Measure("tools.render");
        var states = ExternalToolMenuBuilder.EnumerateTools(nodes).Distinct()
            .ToDictionary(tool => tool, getState);
        if (ReferenceEquals(nodes, _renderedToolNodes)
            && states.Count == _renderedToolStates.Count
            && states.All(pair => _renderedToolStates.TryGetValue(pair.Key, out var old)
                && old.IsVisible == pair.Value.IsVisible))
        {
            if (states.Any(pair => _renderedToolStates[pair.Key] != pair.Value))
            {
                _viewModel.SetExternalToolItems(nodes, tool => states[tool],
                    onSelected, onEditDefinition, onShowInExplorer);
            }
            foreach (var (tool, controls) in _toolControls)
            {
                if (_renderedToolStates[tool] == states[tool]) continue;
                foreach (var control in controls) ApplyToolState(control, states[tool]);
            }

            _renderedToolStates = states;
            return;
        }

        using var rebuild = ShellPerformance.Measure("tools.rebuild");
        _renderedToolNodes = nodes;
        _renderedToolStates = states;
        _toolControls.Clear();
        ClearExternalToolAccelerators();
        ClearExternalToolMenuItems();
        ClearExternalToolContextMenuItems();
        _viewModel.SetExternalToolItems(
            nodes, tool => states[tool], onSelected, onEditDefinition, onShowInExplorer);
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

        RegisterExternalToolAccelerators(nodes, onSelected);
    }

    private void DisposeView()
    {
        if (_uiSettings is not null)
        {
            _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
            _uiSettings = null;
        }

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
        Func<ExternalToolSettings, Task> onSelected)
    {
        var candidatesByShortcut = new Dictionary<
            ExternalToolShortcut,
            List<ExternalToolSettings>>();
        foreach (var tool in ExternalToolMenuBuilder.EnumerateTools(nodes))
        {
            if (!ExternalToolShortcut.TryParse(tool.Shortcut, out var parsedShortcut))
            {
                continue;
            }

            var shortcut = parsedShortcut!;
            if (!candidatesByShortcut.TryGetValue(shortcut, out var candidates))
            {
                candidates = [];
                candidatesByShortcut.Add(shortcut, candidates);
            }

            candidates.Add(tool);
        }

        foreach (var (shortcut, candidates) in candidatesByShortcut)
        {
            var accelerator = new KeyboardAccelerator
            {
                Key = shortcut.Key,
                Modifiers = shortcut.Modifiers
            };
            accelerator.Invoked += (sender, args) =>
            {
                var tool = ExternalToolMenuBuilder.SelectByPriority(
                    candidates,
                    candidate => _runtime.GetExternalToolMenuState(candidate).IsEnabled);
                if (tool is null)
                {
                    return;
                }

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
        foreach (var separator in _windowMenuSeparators)
        {
            _windowMenu.Items.Remove(separator);
        }

        _windowMenuSeparators.Clear();
        foreach (var item in _windowMenuItems)
        {
            _windowMenu.Items.Remove(item);
        }

        _windowMenuItems.Clear();
    }

    private readonly record struct WindowGroupAccent(Color Active, Color Inactive);

    private static class WindowGroupAccentPalette
    {
        private const double HueStep = 60;
        private const double MinimumSaturation = 0.82;

        public static WindowGroupAccent Get(int index, ElementTheme theme, Color baseColor)
        {
            var baseHsl = RgbToHsl(baseColor);
            var hue = NormalizeHue(baseHsl.Hue + (index * HueStep));
            var saturation = Math.Max(baseHsl.Saturation, MinimumSaturation);
            var activeLightness = theme == ElementTheme.Dark
                ? Math.Clamp(baseHsl.Lightness + 0.18, 0.54, 0.68)
                : Math.Clamp(baseHsl.Lightness, 0.40, 0.52);
            var inactiveLightness = theme == ElementTheme.Dark
                ? Math.Clamp(activeLightness - 0.20, 0.30, 0.48)
                : Math.Clamp(activeLightness + 0.14, 0.52, 0.70);

            return new WindowGroupAccent(
                HslToRgb(hue, saturation, activeLightness),
                HslToRgb(hue, saturation, inactiveLightness));
        }

        private static HslColor RgbToHsl(Color color)
        {
            var red = color.R / 255.0;
            var green = color.G / 255.0;
            var blue = color.B / 255.0;
            var max = Math.Max(red, Math.Max(green, blue));
            var min = Math.Min(red, Math.Min(green, blue));
            var delta = max - min;
            var lightness = (max + min) / 2;
            if (delta < double.Epsilon)
            {
                return new HslColor(0, 0, lightness);
            }

            var saturation = delta / (1 - Math.Abs((2 * lightness) - 1));
            var hue = max switch
            {
                var value when value == red => 60 * (((green - blue) / delta) % 6),
                var value when value == green => 60 * (((blue - red) / delta) + 2),
                _ => 60 * (((red - green) / delta) + 4)
            };
            return new HslColor(NormalizeHue(hue), saturation, lightness);
        }

        private static Color HslToRgb(double hue, double saturation, double lightness)
        {
            var chroma = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
            var hueSector = hue / 60;
            var secondComponent = chroma * (1 - Math.Abs((hueSector % 2) - 1));
            var (red, green, blue) = hueSector switch
            {
                < 1 => (chroma, secondComponent, 0.0),
                < 2 => (secondComponent, chroma, 0.0),
                < 3 => (0.0, chroma, secondComponent),
                < 4 => (0.0, secondComponent, chroma),
                < 5 => (secondComponent, 0.0, chroma),
                _ => (chroma, 0.0, secondComponent)
            };
            var match = lightness - (chroma / 2);
            return Color.FromArgb(
                0xff,
                ToByte(red + match),
                ToByte(green + match),
                ToByte(blue + match));
        }

        private static byte ToByte(double value) =>
            (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);

        private static double NormalizeHue(double hue)
        {
            var normalized = hue % 360;
            return normalized < 0 ? normalized + 360 : normalized;
        }

        private readonly record struct HslColor(double Hue, double Saturation, double Lightness);
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
                // Accelerators are registered on the window rather than on the
                // items, so the text beside a tool has to be set explicitly.
                // SplitMenuFlyoutItem is a MenuFlyoutItem, so both shapes of
                // item take it.
                if (menuItem is MenuFlyoutItem acceleratorItem
                    && ExternalToolShortcut.TryParse(tool.Shortcut, out _))
                {
                    acceleratorItem.KeyboardAcceleratorTextOverride = tool.Shortcut;
                }

                items.Add(menuItem);
                TrackToolControl(tool, menuItem);
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

    private List<MenuFlyoutItemBase> CreateExternalToolContextMenuItems(
        IReadOnlyList<ExternalToolMenuItemViewModel> entries)
    {
        var items = new List<MenuFlyoutItemBase>(entries.Count);
        string? previousName = null;
        foreach (var entry in entries)
        {
            if (ExternalToolMenuBuilder.NeedsContextSeparator(previousName, entry.Text))
            {
                items.Add(new MenuFlyoutSeparator());
            }

            previousName = entry.Text;
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

            if (ExternalToolShortcut.TryParse(entry.Tool!.Shortcut, out _))
            {
                item.KeyboardAcceleratorTextOverride = entry.Tool.Shortcut;
            }

            items.Add(item);
            TrackToolControl(entry.Tool, item);
        }

        return items;
    }

    private void TrackToolControl(ExternalToolSettings tool, MenuFlyoutItemBase control)
    {
        if (!_toolControls.TryGetValue(tool, out var controls))
        {
            _toolControls.Add(tool, controls = []);
        }
        controls.Add(control);
    }

    private static void ApplyToolState(MenuFlyoutItemBase control, ExternalToolMenuState state)
    {
        control.IsEnabled = state.IsEnabled;
        AutomationProperties.SetHelpText(control, state.DisabledReason ?? string.Empty);
        ToolTipService.SetToolTip(control, state.DisabledReason);
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

    internal sealed class ViewBoundary :
        IEditorView,
        IWindowMenuView,
        IFindReplaceHost,
        ILanguageModeMenuView,
        IExternalToolMenuView
    {
        private readonly MainWindow _window;

        public ViewBoundary(MainWindow window) =>
            _window = window ?? throw new ArgumentNullException(nameof(window));

        internal Document Document => _window.Document;
        internal IntPtr WindowHandle => _window.WindowHandle;
        internal Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue => _window.DispatcherQueue;
        internal XamlRoot? XamlRoot => _window.XamlRoot;
        internal void SetDocument(Document document) => _window.SetDocument(document);
        internal void SetReadOnly(bool value) => _window._editor.IsReadOnly = value;
        internal void SetDiagnosticLogging(IReadOnlyList<string> logging) =>
            _window.SetDiagnosticLogging(logging);

        public int TabDisplaySize => _window.TabDisplaySize;
        public int? IndentSize => _window.IndentSize;
        public IndentationInputMode IndentationInputMode => _window.IndentationInputMode;
        public bool IsFindBoxFocused => _window.IsFindBoxFocused;
        public string Text => _window.Text;
        public string SelectedText => _window.SelectedText;
        public TextSnapshot Snapshot => _window.Snapshot;
        public TextSelection Selection => _window.Selection;
        public int CaretPosition => _window.CaretPosition;

        public event EventHandler<TextChange>? Edited
        {
            add => _window._editorBuffer.Edited += value;
            remove => _window._editorBuffer.Edited -= value;
        }

        public void SetText(string text) => _window.SetText(text);
        public void SetSelection(TextSelection selection) => _window.SetSelection(selection);
        public void Replace(TextRange range, string replacement) => _window.Replace(range, replacement);
        public void Focus() => _window.Focus();
        public void BeginUndoGroup() => _window.BeginUndoGroup();
        public void EndUndoGroup() => _window.EndUndoGroup();
        public void Undo() => _window.Undo();
        public void Redo() => _window.Redo();
        public void Cut() => _window.Cut();
        public void Copy() => _window.Copy();
        public void Paste() => _window.Paste();
        public void SelectAll() => _window.SelectAll();
        public void ScrollSelectionIntoView() => _window._editor.ScrollSelectionIntoView();
        public void MoveToMatchingBracket() => _window.MoveToMatchingBracket();
        public void Select(TextRange range) => _window.Select(range);
        public void RequestCompletion() => _window.RequestCompletion();
        public void ShowCompletion(CompletionResult completions) => _window.ShowCompletion(completions);
        public void ApplyLanguage(EditorLanguageConfiguration configuration) =>
            _window.ApplyLanguage(configuration);
        public void SetFontFamily(string fontFamily) => _window.SetFontFamily(fontFamily);
        public void SetFontSize(double fontSize) => _window.SetFontSize(fontSize);
        public void RefreshProviders() => _window.RefreshProviders();
        public void SetWordWrap(bool enabled) => _window.SetWordWrap(enabled);
        public void SetTabDisplaySize(int size) => _window.SetTabDisplaySize(size);
        public void SetIndentSize(int? size) => _window.SetIndentSize(size);
        public void SetIndentationInputMode(IndentationInputMode mode) =>
            _window.SetIndentationInputMode(mode);
        public void ApplyEditorConfig(EditorConfigSettings settings) => _window.ApplyEditorConfig(settings);
        public void SetPosition(LineColumn position) => _window.SetPosition(position);
        public void SetStartupPosition(int? line, int? column) =>
            _window.SetStartupPosition(line, column);

        public void RenderWindowMenu(
            IReadOnlyList<WindowMenuEntry> entries,
            bool isAlwaysOnTop,
            Action<string> onSelected) =>
            _window.RenderWindowMenuCore(entries, isAlwaysOnTop, onSelected);

        public void FocusFind() => _window.FocusFind();
        public void FocusEditor() => _window.FocusEditorView();
        public void SelectFindText() => _window.SelectFindText();
        public void ShowNotification(string message, bool replaceMode) =>
            _window.ShowFindNotification(message, replaceMode);
        public void HideNotification() => _window.HideFindNotification();

        public void Render(
            IReadOnlyList<LanguageModeEntry> entries,
            int customModeStartIndex,
            Action<string> onSelected,
            Func<string, Task> onEditDefinition,
            Func<string, Task> onShowInExplorer) =>
            _window.RenderLanguageModes(
                entries, customModeStartIndex, onSelected, onEditDefinition, onShowInExplorer);

        public void Select(string id) => _window.SelectLanguageMode(id);

        public void Render(
            IReadOnlyList<ExternalToolMenuNode> nodes,
            Func<ExternalToolSettings, ExternalToolMenuState> getState,
            Func<ExternalToolSettings, Task> onSelected,
            Func<string, Task> onEditDefinition,
            Func<string, Task> onShowInExplorer) =>
            _window.RenderExternalTools(
                nodes, getState, onSelected, onEditDefinition, onShowInExplorer);
    }

}
