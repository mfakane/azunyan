using Azunyan.Core;
using Azunyan.Syntax;

namespace Azunote;

internal sealed record EditorLanguageConfiguration(
    IReadOnlyList<string> CompletionTriggers,
    ISyntaxProvider? Syntax,
    ICompletionProvider? Completion);

internal sealed record StatusBarState(
    string Position,
    string Encoding,
    string LineEnding,
    string Indentation,
    string LanguageMode,
    string FilePath,
    bool HasFilePath = false);

internal sealed record WindowMenuEntry(
    string Id,
    string DocumentName,
    bool IsCurrent);

internal interface IEditorView : IEditorBuffer
{
    int TabDisplaySize { get; }

    int? IndentSize { get; }

    IndentationInputMode IndentationInputMode { get; }

    void Focus();

    void Undo();

    void Redo();

    void Cut();

    void Copy();

    void Paste();

    void SelectAll();

    void MoveToMatchingBracket();

    void Select(TextRange range);

    void RequestCompletion();

    void ApplyLanguage(EditorLanguageConfiguration configuration);

    void SetFontFamily(string fontFamily);

    void SetFontSize(double fontSize);

    void RefreshProviders();

    void SetWordWrap(bool enabled);

    void SetTabDisplaySize(int size);

    void SetIndentSize(int? size);

    void SetIndentationInputMode(IndentationInputMode mode);

    void ApplyEditorConfig(EditorConfigSettings settings);

    void SetPosition(LineColumn position);

    void SetStartupPosition(int? line, int? column);
}

internal interface IStatusBarView
{
    void Apply(StatusBarState state);

    void ShowFilePathMenu();
}

internal interface IFilePathActions
{
    void CopyFilePath(string filePath);

    Task OpenExplorerAsync(
        string command,
        IReadOnlyList<string> arguments,
        string workingDirectory);

    Task OpenTerminalAsync(
        string command,
        IReadOnlyList<string> arguments,
        string workingDirectory);
}

internal interface IWindowChromeView
{
    bool IsStatusBarVisible { get; }

    void SetTitle(string title);

    void SetStatusBarVisible(bool visible);

    void SetWordWrapLabel(bool enabled);

    void SetTabDisplaySizeLabel(int size);

    void SetIndentSizeLabel(int? size);

    void SetIndentationInputModeLabel(IndentationInputMode mode);
}

internal interface IWindowMenuView
{
    void RenderWindowMenu(
        IReadOnlyList<WindowMenuEntry> entries,
        bool isAlwaysOnTop,
        Action<string> onSelected);
}

internal interface IFindReplaceView
{
    bool IsVisible { get; }

    bool IsFindBoxFocused { get; }

    string FindText { get; set; }

    string ReplaceText { get; set; }

    void Show(bool replace);

    void Close();

    void FocusFind();

    void FocusEditor();

    void SetResult(string message);
}

internal interface ILanguageModeMenuView
{
    void Render(
        IReadOnlyList<LanguageModeEntry> entries,
        int customModeStartIndex,
        Action<string> onSelected,
        Func<string, Task> onEditDefinition,
        Func<string, Task> onShowInExplorer);

    void Select(string id);
}

internal interface IExternalToolMenuView
{
    void Render(
        IReadOnlyList<ExternalToolMenuNode> nodes,
        Func<ExternalToolSettings, ExternalToolMenuState> getState,
        Func<ExternalToolSettings, Task> onSelected,
        Func<string, Task> onEditDefinition,
        Func<string, Task> onShowInExplorer);
}

internal interface IFileDialogService
{
    string? ShowOpen(IReadOnlyList<FileDialogFilter> filters);

    SaveFileDialogResult? ShowSave(
        string suggestedFileName,
        TextEncodingKind defaultEncoding,
        LineEndingKind defaultLineEnding,
        IReadOnlyList<FileDialogFilter> filters,
        string defaultFilterId);
}

internal interface ISettingsFolderOpener
{
    Task OpenAsync(string directory, CancellationToken cancellationToken = default);
}

internal interface IExternalToolDialog
{
    Task<ExternalToolDefinition?> ShowAsync(CancellationToken cancellationToken = default);
}

internal interface IGoToLineDialog
{
    Task<GoToLineTarget?> ShowAsync(string initialText);
}

internal interface IMessageDialog
{
    Task ShowAsync(string title, string message);
}

internal interface IUiDispatcher
{
    bool TryEnqueue(Action action);
}

internal interface IFileChangeMonitor : IDisposable
{
    event EventHandler<FileChangeDetectedEventArgs>? Changed;
}

internal interface IFileChangeMonitorFactory
{
    IFileChangeMonitor Create(string path, bool includeSubdirectories);
}
