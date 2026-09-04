using Microsoft.UI.Input;
using Azunyan.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;

namespace Azunote;

public sealed partial class MainWindow : Window, IDisposable
{
    private const VirtualKey OemOpenBracketKey = (VirtualKey)0xdb;

    private readonly ApplicationCoordinator _application;
    private readonly MainWindowViewAdapter _view;
    private readonly MainWindowRuntime _runtime;
    private readonly AppWindow? _appWindow;
    private bool _allowClose;
    private bool _completionShortcutInvoked;
    private bool _matchingBracketShortcutInvoked;
    private bool _findNextShortcutInvoked;
    private bool _findPreviousShortcutInvoked;
    private bool _disposed;

    internal MainWindowRuntime Runtime => _runtime;

    internal MainWindow(ApplicationCoordinator application)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        InitializeComponent();
        var findNotificationFlyout = RootGrid.Resources["FindNotificationFlyout"] as Flyout
            ?? throw new InvalidOperationException("Find notification flyout is not configured.");
        _view = new MainWindowViewAdapter(
            this,
            Editor,
            RootGrid,
            AuxiliarySplitMenuFlyoutItemStyle,
            FindReplacePanel,
            FindReplaceChevronIcon,
            FindTextBox,
            MatchCaseButton,
            MatchWholeWordButton,
            RegularExpressionButton,
            ReplacePanel,
            ReplaceTextBox,
            FindResultText,
            findNotificationFlyout,
            LanguageModeMenuItem,
            OpenRecentMenuItem,
            WindowMenuItem,
            AlwaysOnTopMenuItem,
            ToolsMenuItem,
            WordWrapMenuItem,
            StatusBarMenuItem,
            TabDisplaySize2MenuItem,
            TabDisplaySize4MenuItem,
            TabDisplaySize8MenuItem,
            StatusTabDisplaySizeMenu,
            IndentSizeAutoMenuItem,
            IndentSize2MenuItem,
            IndentSize4MenuItem,
            IndentSize8MenuItem,
            StatusIndentSizeMenu,
            TabInputModeAutoMenuItem,
            TabInputModeTabMenuItem,
            TabInputModeSpacesMenuItem,
            StatusFilePathMenu,
            StatusBarPanel,
            PositionStatus,
            EncodingStatus,
            LineEndingStatus,
            IndentationStatus,
            LanguageModeStatus,
            FilePathStatus);
        _view.ConfigureTheme();
        _runtime = new MainWindowRuntime(
            _view,
            application.CreateNewDocumentWindowAsync,
            application.OpenFileInNewWindowAsync,
            application.RefreshWindowMenus,
            new WinUiFilePathActions(),
            application.RecordRecentFile,
            application.RecordWordWrap,
            application.RecordStatusBarVisible);
        RegisterKeyboardAccelerators();

        _appWindow = _view.AppWindow;
        if (_appWindow is not null)
        {
            _appWindow.Closing += AppWindow_Closing;
        }

        Closed += MainWindow_Closed;
        Activated += MainWindow_Activated;
    }

    private void RegisterKeyboardAccelerators()
    {
        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += EscapeAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(escape);

        var nextWindow = new KeyboardAccelerator
        {
            Key = VirtualKey.Tab,
            Modifiers = VirtualKeyModifiers.Control
        };
        nextWindow.Invoked += NextWindowAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(nextWindow);

        var previousWindow = new KeyboardAccelerator
        {
            Key = VirtualKey.Tab,
            Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift
        };
        previousWindow.Invoked += PreviousWindowAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(previousWindow);

        var goToMatchingBracket = new KeyboardAccelerator
        {
            Key = OemOpenBracketKey,
            Modifiers = VirtualKeyModifiers.Control
        };
        goToMatchingBracket.Invoked += GoToMatchingBracketAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(goToMatchingBracket);

        var findNext = new KeyboardAccelerator { Key = VirtualKey.F3 };
        findNext.Invoked += FindNextAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(findNext);

        var findPrevious = new KeyboardAccelerator
        {
            Key = VirtualKey.F3,
            Modifiers = VirtualKeyModifiers.Shift
        };
        findPrevious.Invoked += FindPreviousAccelerator_Invoked;
        RootGrid.KeyboardAccelerators.Add(findPrevious);
    }

    private async void OpenButton_Click(object sender, RoutedEventArgs e) =>
        await _runtime.OpenFileAsync();

    private async void NewMenuItem_Click(object sender, RoutedEventArgs e) =>
        await _runtime.NewDocumentAsync();

    private async void SaveButton_Click(object sender, RoutedEventArgs e) =>
        await _runtime.SaveAsync();

    private async void SaveAsButton_Click(object sender, RoutedEventArgs e) =>
        await _runtime.SaveAsAsync();

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => Close();

    private void UndoMenuItem_Click(object sender, RoutedEventArgs e) => _runtime.Undo();

    private void RedoMenuItem_Click(object sender, RoutedEventArgs e) => _runtime.Redo();

    private void CutMenuItem_Click(object sender, RoutedEventArgs e) => _runtime.Cut();

    private void CopyMenuItem_Click(object sender, RoutedEventArgs e) => _runtime.Copy();

    private void PasteMenuItem_Click(object sender, RoutedEventArgs e) => _runtime.Paste();

    private void SelectAllMenuItem_Click(object sender, RoutedEventArgs e) => _runtime.SelectAll();

    private void GoToMatchingBracketMenuItem_Click(object sender, RoutedEventArgs e) =>
        _runtime.MoveToMatchingBracket();

    private void WordWrapMenuItem_Click(object sender, RoutedEventArgs e) => _runtime.ToggleWordWrap();

    private void TabDisplaySizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioMenuFlyoutItem { Tag: string tag }
            && int.TryParse(tag, out var size))
        {
            _runtime.SetTabDisplaySize(size);
        }
    }

    private void IndentSizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem { Tag: string tag })
        {
            return;
        }

        if (string.Equals(tag, "auto", StringComparison.Ordinal))
        {
            _runtime.SetIndentSize(null);
        }
        else if (int.TryParse(tag, out var size))
        {
            _runtime.SetIndentSize(size);
        }
    }

    private void TabInputModeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem { Tag: string tag })
        {
            return;
        }

        var mode = tag switch
        {
            "auto" => IndentationInputMode.Auto,
            "tab" => IndentationInputMode.Tab,
            "spaces" => IndentationInputMode.Spaces,
            _ => (IndentationInputMode?)null
        };
        if (mode is { } selectedMode)
        {
            _runtime.SetIndentationInputMode(selectedMode);
        }
    }

    private void IndentationStatus_Tapped(object sender, TappedRoutedEventArgs e) =>
        _runtime.ShowIndentationSizeMenu();

    private void FilePathStatus_Tapped(object sender, TappedRoutedEventArgs e) =>
        _runtime.ShowFilePathMenu();

    private async void PositionStatus_Tapped(object sender, TappedRoutedEventArgs e) =>
        await _runtime.ShowGoToLineAsync();

    private void CopyFilePathMenuItem_Click(object sender, RoutedEventArgs e) =>
        _runtime.CopyFilePath();

    private async void ShowFileInExplorerMenuItem_Click(object sender, RoutedEventArgs e) =>
        await _runtime.ShowFileInExplorerAsync();

    private async void OpenFolderInTerminalMenuItem_Click(object sender, RoutedEventArgs e) =>
        await _runtime.OpenFolderInTerminalAsync();

    private void StatusBarMenuItem_Click(object sender, RoutedEventArgs e) => _runtime.ToggleStatusBar();

    private void NextWindowMenuItem_Click(object sender, RoutedEventArgs e) =>
        _application.CycleWindow(this, direction: 1);

    private void PreviousWindowMenuItem_Click(object sender, RoutedEventArgs e) =>
        _application.CycleWindow(this, direction: -1);

    private void ShowAllWindowsMenuItem_Click(object sender, RoutedEventArgs e) =>
        _application.ShowAllWindows();

    private void MinimizeAllWindowsMenuItem_Click(object sender, RoutedEventArgs e) =>
        _application.MinimizeAllWindows();

    private void AlwaysOnTopMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _runtime.ToggleAlwaysOnTop();
        _application.RefreshWindowMenus();
    }

    private async void AboutMenuItem_Click(object sender, RoutedEventArgs e) =>
        await _runtime.ShowAboutAsync();

    private async void RunExternalToolMenuItem_Click(object sender, RoutedEventArgs e) =>
        await _runtime.ShowExternalToolDialogAsync();

    private async void PreferencesMenuItem_Click(object sender, RoutedEventArgs e) =>
        await _runtime.OpenPreferencesAsync();

    private void FindButton_Click(object sender, RoutedEventArgs e) =>
        _runtime.ShowFindPanel(replace: false);

    private void ReplaceButton_Click(object sender, RoutedEventArgs e) =>
        _runtime.ShowFindPanel(replace: true);

    private void ShowCompletionMenuItem_Click(object sender, RoutedEventArgs e) =>
        _runtime.ShowCompletion();

    private async void GoToLineMenuItem_Click(object sender, RoutedEventArgs e) =>
        await _runtime.ShowGoToLineAsync();

    private void ShowCompletionAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _completionShortcutInvoked = true;
        _runtime.ShowCompletion();
    }

    private void GoToMatchingBracketAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _matchingBracketShortcutInvoked = true;
        _runtime.MoveToMatchingBracket();
    }

    private void FindNextAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _findNextShortcutInvoked = true;
        _runtime.FindNext();
    }

    private void FindPreviousAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _findPreviousShortcutInvoked = true;
        _runtime.FindPrevious();
    }

    private void EscapeAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_runtime.IsFindPanelVisible)
        {
            args.Handled = true;
            _runtime.CloseFindPanel();
        }
    }

    private void NextWindowAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _application.CycleWindow(this, direction: 1);
    }

    private void PreviousWindowAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _application.CycleWindow(this, direction: -1);
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose || !_runtime.IsDirty)
        {
            return;
        }

        args.Cancel = true;
        if (!await _runtime.TryConfirmCloseAsync())
        {
            return;
        }

        _allowClose = true;
        Close();
    }

    private void MainWindow_Activated(
        object sender,
        Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            _application.WindowActivated(this);
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _application.WindowClosed(this);
        Dispose();
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e) =>
        _runtime.ObserveTextChanged();

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        _runtime.RefreshStatus();
        _runtime.RefreshExternalToolsMenu();
    }

    private void Editor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (HandleFindNavigationKeyDown(e))
        {
            return;
        }

        if (e.Key == OemOpenBracketKey
            && IsKeyDown(VirtualKey.Control)
            && !IsKeyDown(VirtualKey.Menu))
        {
            var acceleratorInvoked = _matchingBracketShortcutInvoked;
            _matchingBracketShortcutInvoked = false;
            e.Handled = true;
            if (!acceleratorInvoked)
            {
                // WinUI does not consistently invoke KeyboardAccelerator for
                // OEM punctuation keys. Keep this fallback in the shell so the
                // editor component remains unaware of the application shortcut.
                _runtime.MoveToMatchingBracket();
            }

            return;
        }

        if (e.Key == VirtualKey.Space && IsKeyDown(VirtualKey.Control))
        {
            var acceleratorInvoked = _completionShortcutInvoked;
            _completionShortcutInvoked = false;
            e.Handled = true;
            if (!acceleratorInvoked)
            {
                _runtime.ShowCompletion();
            }

            return;
        }

        if (e.Key == VirtualKey.Enter
            && _runtime.IsFindPanelVisible
            && _runtime.IsFindBoxFocused)
        {
            _runtime.FindNext();
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
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        var file = items.FirstOrDefault(item => item is StorageFile);
        if (file is not null)
        {
            _runtime.OpenDroppedFile(file.Path);
        }
    }

    private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
        _runtime.OnFindTextChanged();

    private void FindOptionsButton_Click(object sender, RoutedEventArgs e) =>
        _runtime.OnFindOptionsChanged();

    private void CloseFindButton_Click(object sender, RoutedEventArgs e) =>
        _runtime.CloseFindPanel();

    private void FindReplaceModeButton_Click(object sender, RoutedEventArgs e) =>
        _runtime.ToggleFindReplaceMode();

    private void FindPreviousButton_Click(object sender, RoutedEventArgs e) => _runtime.FindPrevious();

    private void FindTextBox_BeforeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        HandleFindTextBoxKeyDown(FindTextBox, _runtime.FindNext, e);
    }

    private void ReplaceTextBox_BeforeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        HandleFindTextBoxKeyDown(ReplaceTextBox, _runtime.ReplaceCurrent, e);
    }

    private void FindNextButton_Click(object sender, RoutedEventArgs e) => _runtime.FindNext();

    private void ReplaceAllButton_Click(object sender, RoutedEventArgs e) => _runtime.ReplaceAll();

    private void ReplaceCurrentButton_Click(object sender, RoutedEventArgs e) => _runtime.ReplaceCurrent();

    private void HandleFindTextBoxKeyDown(
        TextBox textBox,
        Action submit,
        KeyRoutedEventArgs args)
    {
        if (HandleFindNavigationKeyDown(args))
        {
            return;
        }

        if (args.Key != VirtualKey.Enter)
        {
            return;
        }

        args.Handled = true;
        if (IsKeyDown(VirtualKey.Control) && !IsKeyDown(VirtualKey.Menu))
        {
            InsertFindTextNewLine(textBox);
        }
        else
        {
            submit();
        }
    }

    private static void InsertFindTextNewLine(TextBox textBox)
    {
        var selectionStart = textBox.SelectionStart;
        var selectionLength = textBox.SelectionLength;
        // WinUI TextBox stores line breaks as a single CR character. Keep the
        // insertion length aligned with SelectionStart for consecutive input;
        // MainWindowViewAdapter normalizes it back to LF for searching.
        textBox.Text = textBox.Text.Remove(selectionStart, selectionLength)
            .Insert(selectionStart, "\r");
        textBox.SelectionStart = selectionStart + 1;
        textBox.SelectionLength = 0;
    }

    private bool HandleFindNavigationKeyDown(KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.F3)
        {
            return false;
        }

        var previous = IsKeyDown(VirtualKey.Shift);
        var acceleratorInvoked = previous
            ? _findPreviousShortcutInvoked
            : _findNextShortcutInvoked;
        _findNextShortcutInvoked = false;
        _findPreviousShortcutInvoked = false;
        args.Handled = true;
        if (!acceleratorInvoked)
        {
            if (previous)
            {
                _runtime.FindPrevious();
            }
            else
            {
                _runtime.FindNext();
            }
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runtime.Dispose();
    }

    internal string DocumentName => _runtime.DocumentName;

    internal bool IsAlwaysOnTop => _view.IsAlwaysOnTop;

    internal WindowLayoutState? WindowSize => _view.WindowSize;

    internal void ApplyWindowSize(WindowLayoutState size) => _view.ApplyWindowSize(size);

    internal void ApplyViewState(AzunoteState state) => _runtime.ApplyViewState(state);

    internal void RenderRecentFiles(IReadOnlyList<string> paths) =>
        _runtime.RenderRecentFiles(paths, _application.RemoveRecentFile);

    internal void ActivateWindow()
    {
        _view.RestoreIfMinimized();
        Activate();
    }

    internal void MinimizeWindow() => _view.Minimize();

    internal void RestoreIfMinimized() => _view.RestoreIfMinimized();

    internal void FocusEditor() => _view.Focus();

    internal void RenderWindowMenu(
        IReadOnlyList<WindowMenuEntry> entries,
        bool isAlwaysOnTop,
        Action<string> onSelected) =>
        _view.RenderWindowMenu(entries, isAlwaysOnTop, onSelected);
}
