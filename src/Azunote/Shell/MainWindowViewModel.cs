using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Azunyan.Core;

namespace Azunote;

internal sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private IMainWindowActions? _actions;

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
