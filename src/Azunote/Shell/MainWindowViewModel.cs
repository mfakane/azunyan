using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Azunyan.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Azunote;

internal sealed class MainWindowViewModel :
    INotifyPropertyChanged,
    IStatusBarView,
    IWindowChromeView,
    IFindReplaceState
{
    private readonly IMainWindowCommandFactory _commandFactory;
    private IMainWindowActions? _actions;
    private string _title = ApplicationIdentity.DisplayName;
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
    private bool _isReplaceMode;
    private bool _isReadOnly;
    private bool _languageServerFormats;
    private int _runningExternalToolCount;
    private bool _isMatchCase;
    private bool _isInSelection;
    private bool _isMatchWholeWord;
    private bool _isRegularExpression;
    private IReadOnlyList<WindowMenuItemViewModel> _windowItems = [];
    private IReadOnlyList<LanguageModeMenuItemViewModel> _languageModeItems = [];
    private IReadOnlyList<RecentFileMenuItemViewModel> _recentFileItems = [];
    private IReadOnlyList<ExternalToolMenuItemViewModel> _externalToolItems = [];
    private IReadOnlyList<ExternalToolMenuItemViewModel> _externalToolContextItems = [];

    public MainWindowViewModel()
        : this(new XamlMainWindowCommandFactory())
    {
    }

    internal MainWindowViewModel(IMainWindowCommandFactory commandFactory)
    {
        _commandFactory = commandFactory ?? throw new ArgumentNullException(nameof(commandFactory));
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
        GoToNextProblemCommand = Command(() => Actions.GoToProblem(next: true));
        GoToPreviousProblemCommand = Command(() => Actions.GoToProblem(next: false));
        ShowProblemsCommand = Command(() => Actions.ShowProblems());
        MoveLineUpCommand = Command(() => Actions.MoveLines(down: false));
        MoveLineDownCommand = Command(() => Actions.MoveLines(down: true));
        CopyLineUpCommand = Command(() => Actions.DuplicateLines(down: false));
        CopyLineDownCommand = Command(() => Actions.DuplicateLines(down: true));
        DeleteLineCommand = Command(() => Actions.DeleteLines());
        AddNextOccurrenceCommand = Command(() => Actions.AddNextOccurrence());
        ToggleCommentCommand = Command(() => Actions.ToggleComment());
        FormatDocumentCommand = Async(() => Actions.FormatDocumentAsync());
        ReopenWithEncodingCommand = Command(parameter =>
        {
            if (parameter is TextEncoding encoding)
            {
                Actions.ReopenWithEncoding(encoding);
            }
        });
        ReopenWithOtherEncodingCommand = Command(() => Actions.ShowEncodingPicker(reopen: true));
        SaveWithEncodingCommand = Command(parameter =>
        {
            if (parameter is TextEncoding encoding)
            {
                Actions.SaveWithEncoding(encoding);
            }
        });
        SaveWithOtherEncodingCommand = Command(() => Actions.ShowEncodingPicker(reopen: false));
        ChangeLineEndingCommand = Command(parameter =>
        {
            if (Enum.TryParse<LineEndingKind>(parameter?.ToString(), out var lineEnding))
            {
                Actions.ChangeLineEnding(lineEnding);
            }
        });
        ShowFindCommand = Command(() => Actions.ShowFind());
        ShowReplaceCommand = Command(() => Actions.ShowReplace());
        ShowCompletionCommand = Command(() => Actions.ShowCompletion());
        GoToLineCommand = Async(() => Actions.GoToLineAsync());
        ToggleWordWrapCommand = Command(() => Actions.ToggleWordWrap());
        SetTabDisplaySizeCommand = Command(SetTabDisplaySize);
        SetIndentSizeCommand = Command(SetIndentSize);
        SetIndentationInputModeCommand = Command(SetIndentationInputMode);
        ToggleStatusBarCommand = Command(() => Actions.ToggleStatusBar());
        ToggleLanguageServerCommand = Command(() => Actions.ToggleLanguageServer());
        RestartLanguageServerCommand = Command(() => Actions.RestartLanguageServer());
        ShowLanguageServerLogCommand = Async(() => Actions.ShowLanguageServerLogAsync());
        CopyFilePathCommand = Command(() => Actions.CopyFilePath());
        ShowFileInExplorerCommand = Async(() => Actions.ShowFileInExplorerAsync());
        OpenFolderInTerminalCommand = Async(() => Actions.OpenFolderInTerminalAsync());
        RunExternalToolCommand = Async(() => Actions.RunExternalToolAsync());
        ShowCommandPaletteCommand = Command(() => Actions.ShowCommandPalette());
        OpenPreferencesCommand = Async(() => Actions.OpenPreferencesAsync());
        ToggleAlwaysOnTopCommand = Command(() => Actions.ToggleAlwaysOnTop());
        DuplicateWindowCommand = Command(() => Actions.DuplicateWindow());
        ShowAllWindowsCommand = Command(() => Actions.ShowAllWindows());
        MinimizeAllWindowsCommand = Command(() => Actions.MinimizeAllWindows());
        NextWindowCommand = Command(() => Actions.NextWindow());
        PreviousWindowCommand = Command(() => Actions.PreviousWindow());
        ShowAboutCommand = Async(() => Actions.ShowAboutAsync());
        ViewLicenseCommand = Async(() => Actions.ViewLicenseAsync());
        ShowThirdPartyNoticesCommand = Async(() => Actions.ShowThirdPartyNoticesAsync());
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
    public bool IsLineEndingCrLf => _status.LineEndingKind == LineEndingKind.CrLf;
    public bool IsLineEndingLf => _status.LineEndingKind == LineEndingKind.Lf;
    public bool IsLineEndingCr => _status.LineEndingKind == LineEndingKind.Cr;
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
    public bool IsReplaceMode
    {
        get => _isReplaceMode;
        set => SetProperty(ref _isReplaceMode, value, dependentProperties:
            [nameof(ReplacePanelVisibility), nameof(FindReplaceChevronGlyph)]);
    }
    public Visibility ReplacePanelVisibility => IsReplaceMode ? Visibility.Visible : Visibility.Collapsed;
    public string FindReplaceChevronGlyph => IsReplaceMode ? "\uE70E" : "\uE70D";
    public bool IsMatchCase
    {
        get => _isMatchCase;
        set => SetFindOption(ref _isMatchCase, value);
    }
    public bool IsMatchWholeWord
    {
        get => _isMatchWholeWord;
        set => SetFindOption(ref _isMatchWholeWord, value);
    }
    public bool IsRegularExpression
    {
        get => _isRegularExpression;
        set => SetFindOption(ref _isRegularExpression, value);
    }
    public bool IsInSelection
    {
        get => _isInSelection;
        set => SetFindOption(ref _isInSelection, value);
    }
    public IReadOnlyList<WindowMenuItemViewModel> WindowItems => _windowItems;
    public IReadOnlyList<LanguageModeMenuItemViewModel> LanguageModeItems => _languageModeItems;
    public IReadOnlyList<RecentFileMenuItemViewModel> RecentFileItems => _recentFileItems;
    public IReadOnlyList<ExternalToolMenuItemViewModel> ExternalToolItems => _externalToolItems;
    public IReadOnlyList<ExternalToolMenuItemViewModel> ExternalToolContextItems => _externalToolContextItems;
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
    public ICommand GoToNextProblemCommand { get; }
    public ICommand GoToPreviousProblemCommand { get; }
    public ICommand ShowProblemsCommand { get; }
    public ICommand MoveLineUpCommand { get; }
    public ICommand MoveLineDownCommand { get; }
    public ICommand CopyLineUpCommand { get; }
    public ICommand CopyLineDownCommand { get; }
    public ICommand DeleteLineCommand { get; }
    public ICommand AddNextOccurrenceCommand { get; }
    public ICommand ToggleCommentCommand { get; }

    public ICommand FormatDocumentCommand { get; }
    public ICommand ReopenWithEncodingCommand { get; }
    public ICommand SaveWithEncodingCommand { get; }
    public ICommand ReopenWithOtherEncodingCommand { get; }
    public ICommand SaveWithOtherEncodingCommand { get; }
    public ICommand ChangeLineEndingCommand { get; }
    public ICommand ShowFindCommand { get; }
    public ICommand ShowReplaceCommand { get; }
    public ICommand ShowCompletionCommand { get; }
    public ICommand GoToLineCommand { get; }
    public ICommand ToggleWordWrapCommand { get; }
    public ICommand SetTabDisplaySizeCommand { get; }
    public ICommand SetIndentSizeCommand { get; }
    public ICommand SetIndentationInputModeCommand { get; }
    public ICommand ToggleStatusBarCommand { get; }
    public ICommand ToggleLanguageServerCommand { get; }

    public ICommand RestartLanguageServerCommand { get; }

    public ICommand ShowLanguageServerLogCommand { get; }
    public ICommand CopyFilePathCommand { get; }
    public ICommand ShowFileInExplorerCommand { get; }
    public ICommand OpenFolderInTerminalCommand { get; }
    public ICommand RunExternalToolCommand { get; }
    public ICommand ShowCommandPaletteCommand { get; }
    public ICommand OpenPreferencesCommand { get; }
    public ICommand ToggleAlwaysOnTopCommand { get; }
    public ICommand DuplicateWindowCommand { get; }
    public ICommand ShowAllWindowsCommand { get; }
    public ICommand MinimizeAllWindowsCommand { get; }
    public ICommand NextWindowCommand { get; }
    public ICommand PreviousWindowCommand { get; }
    public ICommand ShowAboutCommand { get; }
    public bool IsReadOnly
    {
        get => _isReadOnly;
        set => SetProperty(ref _isReadOnly, value, dependentProperties: [nameof(CanEdit), nameof(CanFormatDocument)]);
    }

    public bool CanEdit => !IsReadOnly;

    /// <summary>Whether the document's language server runs and can format it.</summary>
    public bool CanFormatDocument => _languageServerFormats && CanEdit;
    public string ErrorCountText { get; private set; } = "0";
    public string WarningCountText { get; private set; } = "0";
    public string InformationCountText { get; private set; } = "0";
    public Visibility ProblemsVisibility { get; private set; } = Visibility.Collapsed;

    /// <summary>The message of the problem at the caret, shown beside the counts.</summary>
    public string ProblemAtCaret { get; private set; } = string.Empty;

    public void SetProblems(int errors, int warnings, int informations, string? messageAtCaret)
    {
        ErrorCountText = errors.ToString(CultureInfo.CurrentCulture);
        WarningCountText = warnings.ToString(CultureInfo.CurrentCulture);
        InformationCountText = informations.ToString(CultureInfo.CurrentCulture);
        ProblemsVisibility = errors + warnings + informations > 0 ? Visibility.Visible : Visibility.Collapsed;
        ProblemAtCaret = messageAtCaret ?? string.Empty;
        OnPropertyChanged(nameof(ErrorCountText));
        OnPropertyChanged(nameof(WarningCountText));
        OnPropertyChanged(nameof(InformationCountText));
        OnPropertyChanged(nameof(ProblemsVisibility));
        OnPropertyChanged(nameof(ProblemAtCaret));
    }

    public Visibility LanguageServerVisibility { get; private set; } = Visibility.Collapsed;

    /// <summary>The document's mode has a server it can start in its workspace folder.</summary>
    public bool IsLanguageServerAvailable { get; private set; }

    /// <summary>The server is turned on for the document's workspace folder.</summary>
    public bool IsLanguageServerOn { get; private set; }

    /// <summary>Dimmed while the server is off, full while it runs.</summary>
    public double LanguageServerOpacity { get; private set; } = 1;

    /// <summary>A spinner takes the place of the icon while the server starts.</summary>
    public bool IsLanguageServerBusy { get; private set; }

    public Visibility LanguageServerIconVisibility => IsLanguageServerBusy ? Visibility.Collapsed : Visibility.Visible;

    public Visibility LanguageServerBusyVisibility => IsLanguageServerBusy ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LanguageServerFailureVisibility { get; private set; } = Visibility.Collapsed;

    public string LanguageServerToolTip { get; private set; } = string.Empty;

    public void SetLanguageServer(LanguageServerIndicator indicator, bool formats)
    {
        ArgumentNullException.ThrowIfNull(indicator);
        _languageServerFormats = formats;
        IsLanguageServerAvailable = indicator.State != LanguageServerIndicatorState.Hidden;
        IsLanguageServerOn = indicator.State is not (LanguageServerIndicatorState.Hidden or LanguageServerIndicatorState.Off);
        LanguageServerVisibility = IsLanguageServerAvailable ? Visibility.Visible : Visibility.Collapsed;
        LanguageServerOpacity = indicator.State == LanguageServerIndicatorState.Off ? 0.4 : 1;
        IsLanguageServerBusy = indicator.State == LanguageServerIndicatorState.Starting;
        LanguageServerFailureVisibility = indicator.State == LanguageServerIndicatorState.Failed
            ? Visibility.Visible
            : Visibility.Collapsed;
        var name = Path.GetFileName(indicator.Command);
        LanguageServerToolTip = indicator.State switch
        {
            LanguageServerIndicatorState.Off when indicator.OutsideWorkspace =>
                $"{name} is off for files outside a workspace folder. Click to turn it on.",
            LanguageServerIndicatorState.Off => $"{name} is off for this folder. Click to turn it on.",
            LanguageServerIndicatorState.Starting => $"{name} is starting. Click to turn it off.",
            LanguageServerIndicatorState.Running => $"{name} is running. Click to turn it off.",
            LanguageServerIndicatorState.Failed =>
                $"{name} failed: {indicator.Message}{Environment.NewLine}Click to turn it off.",
            _ => string.Empty
        };
        OnPropertyChanged(nameof(LanguageServerVisibility));
        OnPropertyChanged(nameof(IsLanguageServerAvailable));
        OnPropertyChanged(nameof(IsLanguageServerOn));
        OnPropertyChanged(nameof(LanguageServerOpacity));
        OnPropertyChanged(nameof(IsLanguageServerBusy));
        OnPropertyChanged(nameof(LanguageServerIconVisibility));
        OnPropertyChanged(nameof(LanguageServerBusyVisibility));
        OnPropertyChanged(nameof(LanguageServerFailureVisibility));
        OnPropertyChanged(nameof(LanguageServerToolTip));
        OnPropertyChanged(nameof(CanFormatDocument));
    }

    public int RunningExternalToolCount
    {
        get => _runningExternalToolCount;
        set => SetProperty(ref _runningExternalToolCount, Math.Max(0, value), dependentProperties:
            [nameof(RunningExternalToolCountText), nameof(RunningExternalToolVisibility)]);
    }
    public string RunningExternalToolCountText => RunningExternalToolCount.ToString(CultureInfo.CurrentCulture);
    public Visibility RunningExternalToolVisibility =>
        RunningExternalToolCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    public ICommand ViewLicenseCommand { get; }
    public ICommand ShowThirdPartyNoticesCommand { get; }
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
        OnPropertyChanged(nameof(IsLineEndingCrLf));
        OnPropertyChanged(nameof(IsLineEndingLf));
        OnPropertyChanged(nameof(IsLineEndingCr));
        OnPropertyChanged(nameof(IndentationStatus));
        OnPropertyChanged(nameof(LanguageModeStatus));
        OnPropertyChanged(nameof(FilePathStatus));
        OnPropertyChanged(nameof(HasFilePath));
    }

    void IStatusBarView.Apply(StatusBarState state) => ApplyStatus(state);

    void IWindowChromeView.SetTitle(string title) => Title = title;

    void IWindowChromeView.SetStatusBarVisible(bool visible) => IsStatusBarVisible = visible;

    void IWindowChromeView.SetWordWrapLabel(bool enabled) => IsWordWrapEnabled = enabled;

    void IWindowChromeView.SetTabDisplaySizeLabel(int size) => TabDisplaySize = size;

    void IWindowChromeView.SetIndentSizeLabel(int? size) => IndentSize = size;

    void IWindowChromeView.SetIndentationInputModeLabel(IndentationInputMode mode) =>
        IndentationInputMode = mode;

    bool IFindReplaceState.IsVisible => IsFindPanelVisible;

    FindReplaceOptions IFindReplaceState.Options =>
        (IsMatchCase ? FindReplaceOptions.MatchCase : FindReplaceOptions.None)
        | (IsMatchWholeWord ? FindReplaceOptions.MatchWholeWord : FindReplaceOptions.None)
        | (IsRegularExpression ? FindReplaceOptions.RegularExpression : FindReplaceOptions.None);

    bool IFindReplaceState.IsInSelection => IsInSelection;

    bool IFindReplaceState.IsReplaceMode => IsReplaceMode;

    void IFindReplaceState.Show(bool replace)
    {
        IsFindPanelVisible = true;
        IsReplaceMode = replace;
    }

    void IFindReplaceState.ToggleMode() => IsReplaceMode = !IsReplaceMode;

    void IFindReplaceState.Close()
    {
        IsFindPanelVisible = false;
        IsReplaceMode = false;
    }

    void IFindReplaceState.SetResult(int current, int total) => FindResult = $"{current}/{total}";

    public void SetWindowItems(
        IReadOnlyList<WindowMenuEntry> entries,
        Action<string> onSelected)
    {
        _windowItems = entries.Select(entry => new WindowMenuItemViewModel(
            entry.Id,
            entry.DocumentName,
            entry.IsCurrent,
            entry.IsGroupStart,
            entry.IsDuplicateGroup,
            Command(() => onSelected(entry.Id)))).ToArray();
        OnPropertyChanged(nameof(WindowItems));
    }

    public void SetLanguageModeItems(
        IReadOnlyList<LanguageModeEntry> entries,
        Action<string> onSelected,
        Func<string, Task> onEditDefinition,
        Func<string, Task> onShowInExplorer)
    {
        _languageModeItems = entries.Select(entry => new LanguageModeMenuItemViewModel(
            entry,
            Command(() => onSelected(entry.Id)),
            entry.DefinitionPath is { } path ? Async(() => onEditDefinition(path)) : null,
            entry.DefinitionPath is { } definitionPath ? Async(() => onShowInExplorer(definitionPath)) : null))
            .ToArray();
        OnPropertyChanged(nameof(LanguageModeItems));
    }

    public void SetRecentFileItems(
        IReadOnlyList<string> paths,
        Func<string, Task> onSelected,
        Action<string> onCopyFilePath,
        Func<string, Task> onShowInExplorer,
        Action<string> onRemoved)
    {
        _recentFileItems = paths.Select(path => new RecentFileMenuItemViewModel(
            path,
            Path.GetFileName(path) is { Length: > 0 } name ? name : path,
            Async(() => onSelected(path)),
            Command(() => onCopyFilePath(path)),
            Async(() => onShowInExplorer(path)),
            Command(() => onRemoved(path)))).ToArray();
        OnPropertyChanged(nameof(RecentFileItems));
    }

    public void SetExternalToolItems(
        IReadOnlyList<ExternalToolMenuNode> nodes,
        Func<ExternalToolSettings, ExternalToolMenuState> getState,
        Func<ExternalToolSettings, string, Task> onSelected,
        Func<string, Task> onEditDefinition,
        Func<string, Task> onShowInExplorer)
    {
        _externalToolItems = ExternalToolMenuBuilder.Build(nodes, getState)
            .Select(entry => CreateExternalToolItem(entry, onSelected, onEditDefinition, onShowInExplorer))
            .ToArray();
        _externalToolContextItems = ExternalToolMenuBuilder.BuildFlat(
                nodes, getState, ExternalToolMenuTarget.Context)
            .Select(entry => CreateExternalToolItem(entry, onSelected, onEditDefinition, onShowInExplorer))
            .ToArray();
        OnPropertyChanged(nameof(ExternalToolItems));
        OnPropertyChanged(nameof(ExternalToolContextItems));
    }

    private ExternalToolMenuItemViewModel CreateExternalToolItem(
        ExternalToolMenuEntry entry,
        Func<ExternalToolSettings, string, Task> onSelected,
        Func<string, Task> onEditDefinition,
        Func<string, Task> onShowInExplorer) =>
        new(
            entry.Name,
            entry.Tool,
            entry.State,
            entry.Tool is { } tool ? Async(() => onSelected(tool, entry.PromptTitle)) : null,
            entry.Tool?.DefinitionPath is { } path ? Async(() => onEditDefinition(path)) : null,
            entry.Tool?.DefinitionPath is { } definitionPath ? Async(() => onShowInExplorer(definitionPath)) : null,
            entry.Children.Select(child => CreateExternalToolItem(
                child, onSelected, onEditDefinition, onShowInExplorer)).ToArray());

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

    private void SetFindOption(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName)) return;
        _actions?.FindOptionsChanged();
    }

    private ICommand Command(Action execute) =>
        _commandFactory.Create(_ => execute());

    private ICommand Command(Action<object?> execute) =>
        _commandFactory.Create(execute);

    private ICommand Async(Func<Task> execute) =>
        _commandFactory.CreateAsync(execute);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null,
        params string[] dependentProperties)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        foreach (var dependentProperty in dependentProperties)
        {
            OnPropertyChanged(dependentProperty);
        }
        return true;
    }
}

internal interface IMainWindowCommandFactory
{
    ICommand Create(Action<object?> execute);
    ICommand CreateAsync(Func<Task> execute);
}

internal sealed class XamlMainWindowCommandFactory : IMainWindowCommandFactory
{
    public ICommand Create(Action<object?> execute)
    {
        var command = new XamlUICommand();
        command.ExecuteRequested += (_, args) => execute(args.Parameter);
        return command;
    }

    public ICommand CreateAsync(Func<Task> execute)
    {
        var command = new XamlUICommand();
        var isExecuting = false;
        command.ExecuteRequested += async (_, _) =>
        {
            if (isExecuting) return;
            isExecuting = true;
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
                isExecuting = false;
            }
        };
        return command;
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
    public string LineEnding => $"LineEnding-{_suffix}";
    public string StatusLineEnding => $"StatusLineEnding-{_suffix}";
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
    void GoToProblem(bool next);
    void ShowProblems();
    void MoveLines(bool down);
    void DuplicateLines(bool down);
    void DeleteLines();
    void AddNextOccurrence();
    void ToggleComment();
    Task FormatDocumentAsync();
    void ReopenWithEncoding(TextEncoding encoding);
    void SaveWithEncoding(TextEncoding encoding);
    void ShowEncodingPicker(bool reopen);
    void ChangeLineEnding(LineEndingKind lineEnding);
    void ShowFind();
    void ShowReplace();
    void ShowCompletion();
    Task GoToLineAsync();
    void ToggleWordWrap();
    void SetTabDisplaySize(int size);
    void SetIndentSize(int? size);
    void SetIndentationInputMode(IndentationInputMode mode);
    void ToggleStatusBar();
    void ToggleLanguageServer();

    void RestartLanguageServer();

    Task ShowLanguageServerLogAsync();
    void CopyFilePath();
    Task ShowFileInExplorerAsync();
    Task OpenFolderInTerminalAsync();
    Task RunExternalToolAsync();
    void ShowCommandPalette();
    Task OpenPreferencesAsync();
    void ToggleAlwaysOnTop();
    void DuplicateWindow();
    void ShowAllWindows();
    void MinimizeAllWindows();
    void NextWindow();
    void PreviousWindow();
    Task ShowAboutAsync();
    Task ViewLicenseAsync();
    Task ShowThirdPartyNoticesAsync();
    void FindNext();
    void FindTextChanged();
    void FindOptionsChanged();
    void ReplaceCurrent();
    void ReplaceAll();
    void CloseFind();
}

internal sealed record WindowMenuItemViewModel(
    string Id,
    string Text,
    bool IsCurrent,
    bool IsGroupStart,
    bool IsDuplicateGroup,
    ICommand SelectCommand);

internal sealed record LanguageModeMenuItemViewModel(
    LanguageModeEntry Entry,
    ICommand SelectCommand,
    ICommand? EditDefinitionCommand,
    ICommand? ShowInExplorerCommand);

internal sealed record RecentFileMenuItemViewModel(
    string Path,
    string Text,
    ICommand OpenCommand,
    ICommand CopyFilePathCommand,
    ICommand ShowInExplorerCommand,
    ICommand RemoveCommand);

internal sealed record ExternalToolMenuItemViewModel(
    string Text,
    ExternalToolSettings? Tool,
    ExternalToolMenuState? State,
    ICommand? RunCommand,
    ICommand? EditDefinitionCommand,
    ICommand? ShowInExplorerCommand,
    IReadOnlyList<ExternalToolMenuItemViewModel> Children);
