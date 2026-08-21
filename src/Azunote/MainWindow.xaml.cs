using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace Azunote;

public sealed partial class MainWindow : Window
{
    private readonly IntPtr _windowHandle;
    private readonly AppWindow? _appWindow;
    private string? _filePath;
    private string _savedText = string.Empty;
    private TextEncodingKind _encoding = TextEncodingKind.Utf8;
    private bool _isLoading;
    private bool _allowClose;
    private bool _wordWrapEnabled;

    public MainWindow()
    {
        InitializeComponent();
        RegisterKeyboardAccelerators();

        _windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        if (_appWindow is not null)
        {
            _appWindow.Closing += AppWindow_Closing;
        }

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

    public async Task OpenStartupDocumentAsync(string path)
    {
        try
        {
            await LoadDocumentAsync(path);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Could not open the file", exception.Message);
        }
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
        Editor.Undo();
    }

    private void RedoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Editor.Focus(FocusState.Programmatic);
        Editor.Redo();
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
            Editor.Text = string.Empty;
        }
        finally
        {
            _isLoading = false;
        }

        _filePath = null;
        _savedText = string.Empty;
        _encoding = TextEncodingKind.Utf8;
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
            Editor.Text = document.Text;
        }
        finally
        {
            _isLoading = false;
        }

        _filePath = Path.GetFullPath(path);
        _savedText = document.Text;
        _encoding = document.Encoding;
        UpdateStatus(document.LineEnding);
        UpdateTitle();
        Editor.Focus(FocusState.Programmatic);
        Editor.SelectionStart = 0;
        Editor.SelectionLength = 0;
    }

    private async Task<bool> SaveAsync()
    {
        if (_filePath is null)
        {
            return await SaveAsAsync();
        }

        try
        {
            await TextFileService.WriteAsync(_filePath, Editor.Text, _encoding);
            _savedText = Editor.Text;
            UpdateStatus();
            UpdateTitle();
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
        var picker = new FileSavePicker
        {
            SuggestedFileName = _filePath is null ? "Untitled.txt" : Path.GetFileName(_filePath),
            DefaultFileExtension = ".txt"
        };
        picker.FileTypeChoices.Add("Text files", new List<string> { ".txt", ".md", ".log", ".json", ".xml", ".csv" });
        InitializeWithWindow.Initialize(picker, _windowHandle);

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return false;
        }

        try
        {
            await TextFileService.WriteAsync(file.Path, Editor.Text, _encoding);
            _filePath = Path.GetFullPath(file.Path);
            _savedText = Editor.Text;
            UpdateStatus();
            UpdateTitle();
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
        var text = Editor.Text;
        var selectionStart = Math.Clamp(Editor.SelectionStart, 0, text.Length);
        var line = 1;
        var column = 1;

        for (var index = 0; index < selectionStart; index++)
        {
            if (text[index] == '\r')
            {
                line++;
                column = 1;

                if (index + 1 < selectionStart && text[index + 1] == '\n')
                {
                    index++;
                }
            }
            else if (text[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        PositionStatus.Text = $"Ln {line}, Col {column}";
        EncodingStatus.Text = TextFileService.GetEncodingDisplayName(_encoding);
        LineEndingStatus.Text = TextFileService.GetLineEndingDisplayName(lineEnding ?? TextFileService.DetectLineEnding(text));
        FilePathStatus.Text = _filePath ?? "Untitled";
    }

    private void UpdateTitle()
    {
        var name = _filePath is null ? "Untitled" : Path.GetFileName(_filePath);
        var dirtyMarker = IsDirty ? "*" : string.Empty;
        if (_appWindow is not null)
        {
            _appWindow.Title = $"{dirtyMarker}{name} — Azunote";
        }
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = RootGrid.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
