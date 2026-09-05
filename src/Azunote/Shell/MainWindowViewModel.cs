using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Azunyan.Core;
using Microsoft.UI.Xaml;

namespace Azunote;

internal sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private IMainWindowActions? _actions;
    private string _title = "Azunote";
    private bool _isWordWrapEnabled;
    private bool _isStatusBarVisible = true;
    private bool _isAlwaysOnTop;
    private int _tabDisplaySize = 4;
    private int? _indentSize;
    private IndentationInputMode _indentationInputMode;
    private StatusBarState _status = new(
        "Ln 1, Col 1", "UTF-8", "LF", "Spaces: 2", "Plain Text", "Untitled");
    private bool _isFindPanelVisible;
    private string _findText = string.Empty;
    private string _replaceText = string.Empty;
    private string _findResult = string.Empty;

    public MainWindowViewModel()
    {
        Groups = new MainWindowRadioGroupNames();
        OpenCommand = Async(() => Actions.OpenFileAsync());
        NewCommand = Async(() => Actions.NewDocumentAsync());
        SaveCommand = Async(() => Actions.SaveAsync());
        SaveAsCommand = Async(() => Actions.SaveAsAsync());
        ExitCommand = Command(() => Actions.Exit());
        UndoCommand = Command(() => Actions.Undo());
        RedoCommand = Command(() => Actions.Redo());
        CutCommand = Command(() => Actions.Cut());
        CopyCommand = Command(() => Actions.Copy());
        PasteCommand = Command(() => Actions.Paste());
        SelectAllCommand = Command(() => Actions.SelectAll());
        GoToMatchingBracketCommand = Command(() => Actions.GoToMatchingBracket());
        ShowFindCommand = Command(() => Actions.ShowFind());
        ShowReplaceCommand = Command(() => Actions.ShowReplace());
        ShowCompletionCommand = Command(() => Actions.ShowCompletion());
        GoToLineCommand = Async(() => Actions.GoToLineAsync());
        ToggleWordWrapCommand = Command(() => Actions.ToggleWordWrap());
        SetTabDisplaySizeCommand = Command(SetTabDisplaySize);
        SetIndentSizeCommand = Command(SetIndentSize);
        SetIndentationInputModeCommand = Command(SetIndentationInputMode);
        ToggleStatusBarCommand = Command(() => Actions.ToggleStatusBar());
        CopyFilePathCommand = Command(() => Actions.CopyFilePath());
        ShowFileInExplorerCommand = Async(() => Actions.ShowFileInExplorerAsync());
        OpenFolderInTerminalCommand = Async(() => Actions.OpenFolderInTerminalAsync());
        RunExternalToolCommand = Async(() => Actions.RunExternalToolAsync());
        OpenPreferencesCommand = Async(() => Actions.OpenPreferencesAsync());
        ToggleAlwaysOnTopCommand = Command(() => Actions.ToggleAlwaysOnTop());
        DuplicateWindowCommand = Command(() => Actions.DuplicateWindow());
        ShowAllWindowsCommand = Command(() => Actions.ShowAllWindows());
        MinimizeAllWindowsCommand = Command(() => Actions.MinimizeAllWindows());
        NextWindowCommand = Command(() => Actions.NextWindow());
        PreviousWindowCommand = Command(() => Actions.PreviousWindow());
        ShowAboutCommand = Async(() => Actions.ShowAboutAsync());
        FindNextCommand = Command(() => Actions.FindNext());
        ReplaceCurrentCommand = Command(() => Actions.ReplaceCurrent());
        ReplaceAllCommand = Command(() => Actions.ReplaceAll());
        CloseFindCommand = Command(() => Actions.CloseFind());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindowRadioGroupNames Groups { get; }
    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public bool IsWordWrapEnabled { get => _isWordWrapEnabled; set => SetProperty(ref _isWordWrapEnabled, value); }
    public bool IsStatusBarVisible { get => _isStatusBarVisible; set => SetProperty(ref _isStatusBarVisible, value, dependentProperties: [nameof(StatusBarVisibility)]); }
    public Visibility StatusBarVisibility => IsStatusBarVisible ? Visibility.Visible : Visibility.Collapsed;
    public bool IsAlwaysOnTop { get => _isAlwaysOnTop; set => SetProperty(ref _isAlwaysOnTop, value); }
    public int TabDisplaySize
    {
        get => _tabDisplaySize;
        set => SetProperty(ref _tabDisplaySize, value, dependentProperties:
            [nameof(IsTabDisplaySize2), nameof(IsTabDisplaySize4), nameof(IsTabDisplaySize8)]);
    }
    public bool IsTabDisplaySize2 => TabDisplaySize == 2;
    public bool IsTabDisplaySize4 => TabDisplaySize == 4;
    public bool IsTabDisplaySize8 => TabDisplaySize == 8;
    public int? IndentSize
    {
        get => _indentSize;
        set => SetProperty(ref _indentSize, value, dependentProperties:
            [nameof(IsIndentSizeAuto), nameof(IsIndentSize2), nameof(IsIndentSize4), nameof(IsIndentSize8)]);
    }
    public bool IsIndentSizeAuto => IndentSize is null;
    public bool IsIndentSize2 => IndentSize == 2;
    public bool IsIndentSize4 => IndentSize == 4;
    public bool IsIndentSize8 => IndentSize == 8;
    public IndentationInputMode IndentationInputMode
    {
        get => _indentationInputMode;
        set => SetProperty(ref _indentationInputMode, value, dependentProperties:
            [nameof(IsIndentationModeAuto), nameof(IsIndentationModeTab), nameof(IsIndentationModeSpaces)]);
    }
    public bool IsIndentationModeAuto => IndentationInputMode == IndentationInputMode.Auto;
    public bool IsIndentationModeTab => IndentationInputMode == IndentationInputMode.Tab;
    public bool IsIndentationModeSpaces => IndentationInputMode == IndentationInputMode.Spaces;
    public string PositionStatus => _status.Position;
    public string EncodingStatus => _status.Encoding;
    public string LineEndingStatus => _status.LineEnding;
    public string IndentationStatus => _status.Indentation;
    public string LanguageModeStatus => _status.LanguageMode;
    public string FilePathStatus => _status.FilePath;
    public bool HasFilePath => _status.HasFilePath;
    public bool IsFindPanelVisible { get => _isFindPanelVisible; set => SetProperty(ref _isFindPanelVisible, value, dependentProperties: [nameof(FindPanelVisibility)]); }
    public Visibility FindPanelVisibility => IsFindPanelVisible ? Visibility.Visible : Visibility.Collapsed;
    public string FindText
    {
        get => _findText;
        set
        {
            if (string.Equals(_findText, value, StringComparison.Ordinal)) return;
            _findText = value;
            OnPropertyChanged();
            _actions?.FindTextChanged();
        }
    }
    public string ReplaceText { get => _replaceText; set => SetProperty(ref _replaceText, value); }
    public string FindResult { get => _findResult; set => SetProperty(ref _findResult, value); }
    public ICommand OpenCommand { get; }
    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand CutCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand PasteCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand GoToMatchingBracketCommand { get; }
    public ICommand ShowFindCommand { get; }
    public ICommand ShowReplaceCommand { get; }
    public ICommand ShowCompletionCommand { get; }
    public ICommand GoToLineCommand { get; }
    public ICommand ToggleWordWrapCommand { get; }
    public ICommand SetTabDisplaySizeCommand { get; }
    public ICommand SetIndentSizeCommand { get; }
    public ICommand SetIndentationInputModeCommand { get; }
    public ICommand ToggleStatusBarCommand { get; }
    public ICommand CopyFilePathCommand { get; }
    public ICommand ShowFileInExplorerCommand { get; }
    public ICommand OpenFolderInTerminalCommand { get; }
    public ICommand RunExternalToolCommand { get; }
    public ICommand OpenPreferencesCommand { get; }
    public ICommand ToggleAlwaysOnTopCommand { get; }
    public ICommand DuplicateWindowCommand { get; }
    public ICommand ShowAllWindowsCommand { get; }
    public ICommand MinimizeAllWindowsCommand { get; }
    public ICommand NextWindowCommand { get; }
    public ICommand PreviousWindowCommand { get; }
    public ICommand ShowAboutCommand { get; }
    public ICommand FindNextCommand { get; }
    public ICommand ReplaceCurrentCommand { get; }
    public ICommand ReplaceAllCommand { get; }
    public ICommand CloseFindCommand { get; }

    public void Attach(IMainWindowActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        if (_actions is not null)
        {
            throw new InvalidOperationException("The main window view model is already attached.");
        }

        _actions = actions;
    }

    public void ApplyStatus(StatusBarState status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (_status == status) return;
        _status = status;
        OnPropertyChanged(nameof(PositionStatus));
        OnPropertyChanged(nameof(EncodingStatus));
        OnPropertyChanged(nameof(LineEndingStatus));
        OnPropertyChanged(nameof(IndentationStatus));
        OnPropertyChanged(nameof(LanguageModeStatus));
        OnPropertyChanged(nameof(FilePathStatus));
        OnPropertyChanged(nameof(HasFilePath));
    }

    private IMainWindowActions Actions => _actions
        ?? throw new InvalidOperationException("The main window view model is not attached.");

    private void SetTabDisplaySize(object? parameter)
    {
        if (TryGetInt(parameter, out var size) && size is 2 or 4 or 8)
        {
            Actions.SetTabDisplaySize(size);
        }
    }

    private void SetIndentSize(object? parameter)
    {
        if (string.Equals(parameter?.ToString(), "auto", StringComparison.Ordinal))
        {
            Actions.SetIndentSize(null);
        }
        else if (TryGetInt(parameter, out var size) && size is 2 or 4 or 8)
        {
            Actions.SetIndentSize(size);
        }
    }

    private void SetIndentationInputMode(object? parameter)
    {
        var mode = parameter?.ToString() switch
        {
            "auto" => IndentationInputMode.Auto,
            "tab" => IndentationInputMode.Tab,
            "spaces" => IndentationInputMode.Spaces,
            _ => (IndentationInputMode?)null
        };
        if (mode is { } value)
        {
            Actions.SetIndentationInputMode(value);
        }
    }

    private static bool TryGetInt(object? parameter, out int value) =>
        int.TryParse(parameter?.ToString(), out value);

    private static MainWindowCommand Command(Action execute) =>
        new MainWindowCommand(_ => execute());

    private static MainWindowCommand Command(Action<object?> execute) =>
        new MainWindowCommand(execute);

    private static MainWindowAsyncCommand Async(Func<Task> execute) =>
        new MainWindowAsyncCommand(execute);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null,
        params string[] dependentProperties)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
        foreach (var dependentProperty in dependentProperties)
        {
            OnPropertyChanged(dependentProperty);
        }
    }
}

internal sealed class MainWindowRadioGroupNames
{
    private readonly string _suffix = Guid.NewGuid().ToString("N");

    public string StatusTabDisplaySize => $"StatusTabDisplaySize-{_suffix}";
    public string StatusIndentSize => $"StatusIndentSize-{_suffix}";
    public string TabDisplaySize => $"TabDisplaySize-{_suffix}";
    public string IndentSize => $"IndentSize-{_suffix}";
    public string IndentationInputMode => $"IndentationInputMode-{_suffix}";
    public string LanguageModes => $"LanguageModes-{_suffix}";
    public string OpenWindows => $"OpenWindows-{_suffix}";
}

internal interface IMainWindowActions
{
    Task OpenFileAsync();
    Task NewDocumentAsync();
    Task SaveAsync();
    Task SaveAsAsync();
    void Exit();
    void Undo();
    void Redo();
    void Cut();
    void Copy();
    void Paste();
    void SelectAll();
    void GoToMatchingBracket();
    void ShowFind();
    void ShowReplace();
    void ShowCompletion();
    Task GoToLineAsync();
    void ToggleWordWrap();
    void SetTabDisplaySize(int size);
    void SetIndentSize(int? size);
    void SetIndentationInputMode(IndentationInputMode mode);
    void ToggleStatusBar();
    void CopyFilePath();
    Task ShowFileInExplorerAsync();
    Task OpenFolderInTerminalAsync();
    Task RunExternalToolAsync();
    Task OpenPreferencesAsync();
    void ToggleAlwaysOnTop();
    void DuplicateWindow();
    void ShowAllWindows();
    void MinimizeAllWindows();
    void NextWindow();
    void PreviousWindow();
    Task ShowAboutAsync();
    void FindNext();
    void FindTextChanged();
    void ReplaceCurrent();
    void ReplaceAll();
    void CloseFind();
}

internal sealed class MainWindowCommand(Action<object?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute(parameter);
}

internal sealed class MainWindowAsyncCommand(Func<Task> execute) : ICommand
{
    private bool _isExecuting;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_isExecuting;

    public async void Execute(object? parameter)
    {
        if (_isExecuting) return;
        _isExecuting = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await execute();
        }
        catch (Exception exception)
        {
            ErrorReporter.LogException("Main window command", exception);
        }
        finally
        {
            _isExecuting = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
