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

public sealed partial class MainWindow : Window, IDisposable, IMainWindowActions
{
    private const VirtualKey OemOpenBracketKey = (VirtualKey)0xdb;

    private readonly ApplicationCoordinator _application;
    private readonly MainWindowRuntime _runtime;
    private AppWindow? _appWindow;
    private bool _allowClose;
    private bool _completionShortcutInvoked;
    private bool _matchingBracketShortcutInvoked;
    private bool _findNextShortcutInvoked;
    private bool _findPreviousShortcutInvoked;
    private bool _disposed;

    internal MainWindowRuntime Runtime => _runtime;
    internal MainWindowViewModel ViewModel { get; } = new();

    internal MainWindow(
        ApplicationCoordinator application,
        DocumentSession? session = null,
        Document? document = null)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        InitializeComponent();
        InitializeView();
        ConfigureTheme();
        _runtime = new MainWindowRuntime(
            this,
            ViewModel,
            application.CreateNewDocumentWindowAsync,
            application.OpenFileInNewWindowAsync,
            application.OpenTextInNewWindowAsync,
            application.RefreshWindowMenus,
            new WinUiFilePathActions(),
            application.RecordRecentFile,
            application.RecordWordWrap,
            application.RecordStatusBarVisible,
            session,
            document,
            path => application.OpenFileAsync(this, path));
        ViewModel.Attach(this);
        RegisterKeyboardAccelerators();

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

    private void IndentationStatus_Tapped(object sender, TappedRoutedEventArgs e) =>
        _runtime.ShowIndentationSizeMenu();

    private void FilePathStatus_Tapped(object sender, TappedRoutedEventArgs e) =>
        _runtime.ShowFilePathMenu();

    private async void PositionStatus_Tapped(object sender, TappedRoutedEventArgs e) =>
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
        if (_allowClose || !_runtime.IsDirty || _application.HasOtherView(this))
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
        _isWindowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        ApplyWindowGroupAccentCore();
        if (_isWindowActive)
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

    private void FindReplaceModeButton_Click(object sender, RoutedEventArgs e) =>
        _runtime.ToggleFindReplaceMode();

    private void FindPreviousButton_Click(object sender, RoutedEventArgs e) =>
        _runtime.FindPrevious();

    private void FindNextButton_Click(object sender, RoutedEventArgs e) =>
        _runtime.FindNext();

    private void CloseFindButton_Click(object sender, RoutedEventArgs e) =>
        _runtime.CloseFindPanel();

    private void FindTextBox_BeforeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        HandleFindTextBoxKeyDown(FindTextBox, _runtime.FindNext, e);
    }

    private void ReplaceTextBox_BeforeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        HandleFindTextBoxKeyDown(ReplaceTextBox, _runtime.ReplaceCurrent, e);
    }

    private void HandleFindTextBoxKeyDown(
        TextBox textBox,
        Action submit,
        KeyRoutedEventArgs args)
    {
        if (HandleFindNavigationKeyDown(args))
        {
            return;
        }

        if (args.Key != VirtualKey.Enter) return;
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
        textBox.Text = textBox.Text.Remove(selectionStart, selectionLength)
            .Insert(selectionStart, "\r");
        textBox.SelectionStart = selectionStart + 1;
        textBox.SelectionLength = 0;
    }

    private bool HandleFindNavigationKeyDown(KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.F3) return false;
        var previous = IsKeyDown(VirtualKey.Shift);
        var acceleratorInvoked = previous
            ? _findPreviousShortcutInvoked
            : _findNextShortcutInvoked;
        _findNextShortcutInvoked = false;
        _findPreviousShortcutInvoked = false;
        args.Handled = true;
        if (!acceleratorInvoked)
        {
            if (previous) _runtime.FindPrevious();
            else _runtime.FindNext();
        }
        return true;
    }

    Task IMainWindowActions.OpenFileAsync() => _runtime.OpenFileAsync();

    Task IMainWindowActions.NewDocumentAsync() => _runtime.NewDocumentAsync();

    async Task IMainWindowActions.SaveAsync() => await _runtime.SaveAsync();

    async Task IMainWindowActions.SaveAsAsync() => await _runtime.SaveAsAsync();

    void IMainWindowActions.Exit() => Close();

    void IMainWindowActions.Undo() => _runtime.Undo();

    void IMainWindowActions.Redo() => _runtime.Redo();

    void IMainWindowActions.Cut() => _runtime.Cut();

    void IMainWindowActions.Copy() => _runtime.Copy();

    void IMainWindowActions.Paste() => _runtime.Paste();

    void IMainWindowActions.SelectAll() => _runtime.SelectAll();

    void IMainWindowActions.GoToMatchingBracket() => _runtime.MoveToMatchingBracket();

    void IMainWindowActions.ShowFind() => _runtime.ShowFindPanel(replace: false);

    void IMainWindowActions.ShowReplace() => _runtime.ShowFindPanel(replace: true);

    void IMainWindowActions.ShowCompletion() => _runtime.ShowCompletion();

    Task IMainWindowActions.GoToLineAsync() => _runtime.ShowGoToLineAsync();

    void IMainWindowActions.ToggleWordWrap() => _runtime.ToggleWordWrap();

    void IMainWindowActions.SetTabDisplaySize(int size) => _runtime.SetTabDisplaySize(size);

    void IMainWindowActions.SetIndentSize(int? size) => _runtime.SetIndentSize(size);

    void IMainWindowActions.SetIndentationInputMode(IndentationInputMode mode) =>
        _runtime.SetIndentationInputMode(mode);

    void IMainWindowActions.ToggleStatusBar() => _runtime.ToggleStatusBar();

    void IMainWindowActions.CopyFilePath() => _runtime.CopyFilePath();

    Task IMainWindowActions.ShowFileInExplorerAsync() => _runtime.ShowFileInExplorerAsync();

    Task IMainWindowActions.OpenFolderInTerminalAsync() => _runtime.OpenFolderInTerminalAsync();

    Task IMainWindowActions.RunExternalToolAsync() => _runtime.ShowExternalToolDialogAsync();

    Task IMainWindowActions.OpenPreferencesAsync() => _runtime.OpenPreferencesAsync();

    void IMainWindowActions.ToggleAlwaysOnTop()
    {
        _runtime.ToggleAlwaysOnTop();
        _application.RefreshWindowMenus();
    }

    void IMainWindowActions.DuplicateWindow() => _application.DuplicateWindow(this);

    void IMainWindowActions.ShowAllWindows() => _application.ShowAllWindows();

    void IMainWindowActions.MinimizeAllWindows() => _application.MinimizeAllWindows();

    void IMainWindowActions.NextWindow() => _application.CycleWindow(this, direction: 1);

    void IMainWindowActions.PreviousWindow() => _application.CycleWindow(this, direction: -1);

    Task IMainWindowActions.ShowAboutAsync() => _runtime.ShowAboutAsync();

    Task IMainWindowActions.ViewLicenseAsync() => _runtime.ViewLicenseAsync();

    Task IMainWindowActions.ShowThirdPartyNoticesAsync() => _runtime.ShowThirdPartyNoticesAsync();

    void IMainWindowActions.FindNext() => _runtime.FindNext();

    void IMainWindowActions.FindTextChanged() => _runtime.OnFindTextChanged();

    void IMainWindowActions.FindOptionsChanged() => _runtime.OnFindOptionsChanged();

    void IMainWindowActions.ReplaceCurrent() => _runtime.ReplaceCurrent();

    void IMainWindowActions.ReplaceAll() => _runtime.ReplaceAll();

    void IMainWindowActions.CloseFind() => _runtime.CloseFindPanel();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runtime.Dispose();
        DisposeView();
    }

    internal string DocumentName => _runtime.DocumentName;

    internal bool IsAlwaysOnTop => IsAlwaysOnTopView;

    internal WindowLayoutState? WindowSize => WindowSizeView;

    internal void ApplyWindowSize(WindowLayoutState size) => ApplyWindowSizeView(size);

    internal void ApplyViewState(AzunoteState state) => _runtime.ApplyViewState(state);

    internal void RenderRecentFiles(IReadOnlyList<string> paths) =>
        _runtime.RenderRecentFiles(paths, _application.RemoveRecentFile);

    internal void ActivateWindow()
    {
        RestoreIfMinimizedView();
        Activate();
    }

    internal void MinimizeWindow() => MinimizeView();

    internal void RestoreIfMinimized() => RestoreIfMinimizedView();

    internal void FocusEditor() => Focus();

    internal void RenderWindowMenu(
        IReadOnlyList<WindowMenuEntry> entries,
        bool isAlwaysOnTop,
        Action<string> onSelected) =>
        RenderWindowMenuCore(entries, isAlwaysOnTop, onSelected);

}
