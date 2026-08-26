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
    string FilePath);

internal interface IEditorView : IEditorBuffer
{
    void Focus();

    void Undo();

    void Redo();

    void Cut();

    void Copy();

    void Paste();

    void SelectAll();

    void Select(TextRange range);

    void RequestCompletion();

    void ApplyLanguage(EditorLanguageConfiguration configuration);

    void RefreshProviders();

    void SetWordWrap(bool enabled);

    void SetStartupPosition(int? line, int? column);
}

internal interface IStatusBarView
{
    void Apply(StatusBarState state);
}

internal interface IWindowChromeView
{
    bool IsStatusBarVisible { get; }

    void SetTitle(string title);

    void SetStatusBarVisible(bool visible);

    void SetWordWrapLabel(bool enabled);
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
        Action<string> onSelected);

    void Select(string id);
}

internal interface IExternalToolMenuView
{
    void Render(
        IReadOnlyList<ExternalToolMenuNode> nodes,
        Func<ExternalToolSettings, Task> onSelected);
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
