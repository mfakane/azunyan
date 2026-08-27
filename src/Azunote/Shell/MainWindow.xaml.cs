using Microsoft.UI.Input;
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
    private readonly ApplicationCoordinator _application;
    private readonly MainWindowViewAdapter _view;
    private readonly MainWindowRuntime _runtime;
    private readonly AppWindow? _appWindow;
    private bool _allowClose;
    private bool _completionShortcutInvoked;
    private bool _disposed;

    internal MainWindowRuntime Runtime => _runtime;

    internal MainWindow(ApplicationCoordinator application)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        InitializeComponent();
        _view = new MainWindowViewAdapter(
            this,
            Editor,
            RootGrid,
            FindPanel,
            FindTextBox,
            ReplaceTextBox,
            FindResultText,
            LanguageModeMenuItem,
            WindowMenuItem,
            AlwaysOnTopMenuItem,
            ToolsMenuItem,
            WordWrapMenuItem,
            TabDisplaySize2MenuItem,
            TabDisplaySize4MenuItem,
            TabDisplaySize8MenuItem,
            IndentSizeAutoMenuItem,
            IndentSize2MenuItem,
            IndentSize4MenuItem,
            IndentSize8MenuItem,
            StatusBarPanel,
            PositionStatus,
            EncodingStatus,
            LineEndingStatus,
            IndentationStatus,
            FilePathStatus);
        _view.ConfigureTheme();
        _runtime = new MainWindowRuntime(
            _view,
            application.CreateNewDocumentWindowAsync,
            application.OpenFileInNewWindowAsync,
            application.RefreshWindowMenus);
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

    private void WordWrapMenuItem_Click(object sender, RoutedEventArgs e) => _runtime.ToggleWordWrap();

    private void TabDisplaySizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem { Tag: string tag }
            && int.TryParse(tag, out var size))
        {
            _runtime.SetTabDisplaySize(size);
        }
    }

    private void IndentSizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleMenuFlyoutItem { Tag: string tag })
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

    private void ReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_runtime.IsFindPanelVisible)
        {
            _runtime.ShowFindPanel(replace: true);
            return;
        }

        _runtime.ReplaceCurrent();
    }

    private void ShowCompletionMenuItem_Click(object sender, RoutedEventArgs e) =>
        _runtime.ShowCompletion();

    private void ShowCompletionAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _completionShortcutInvoked = true;
        _runtime.ShowCompletion();
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

    private void CloseFindButton_Click(object sender, RoutedEventArgs e) =>
        _runtime.CloseFindPanel();

    private void FindTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            _runtime.FindNext();
            e.Handled = true;
        }
    }

    private void ReplaceTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            _runtime.ReplaceCurrent();
            e.Handled = true;
        }
    }

    private void FindNextButton_Click(object sender, RoutedEventArgs e) => _runtime.FindNext();

    private void ReplaceAllButton_Click(object sender, RoutedEventArgs e) => _runtime.ReplaceAll();

    private void ReplaceCurrentButton_Click(object sender, RoutedEventArgs e) => _runtime.ReplaceCurrent();

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
