using Azunote;
using Azunyan.Core;

namespace Azunote.Tests.Shell;

internal sealed class FakeEditorView : IEditorView
{
    private Document _document;

    public FakeEditorView(string text = "")
    {
        _document = new Document(text);
    }

    public string Text => _document.Text;

    public string SelectedText => _document.Snapshot.GetText(_document.Selection.Range);

    public TextSnapshot Snapshot => _document.Snapshot;

    public TextSelection Selection => _document.Selection;

    public int CaretPosition => _document.CaretPosition;

    public int FocusCount { get; private set; }

    public int RefreshProvidersCount { get; private set; }

    public bool WordWrapEnabled { get; private set; }

    public int TabDisplaySize { get; private set; } = 4;

    public int? IndentSize { get; private set; }

    public IndentationInputMode IndentationInputMode { get; private set; }

    public EditorLanguageConfiguration? LanguageConfiguration { get; private set; }

    public TextSelection? LastStartupSelection { get; private set; }

    public void SetText(string text) => _document = new Document(text);

    public void SetSelection(TextSelection selection) => _document.Selection = selection;

    public void Replace(TextRange range, string replacement) => _document.Replace(range, replacement);

    public void Focus() => FocusCount++;

    public void Undo()
    {
    }

    public void Redo()
    {
    }

    public void Cut()
    {
    }

    public void Copy()
    {
    }

    public void Paste()
    {
    }

    public void SelectAll() => _document.Select(new TextRange(0, Text.Length));

    public void Select(TextRange range) => _document.Select(range);

    public void RequestCompletion()
    {
    }

    public void ApplyLanguage(EditorLanguageConfiguration configuration) =>
        LanguageConfiguration = configuration;

    public void RefreshProviders() => RefreshProvidersCount++;

    public void SetWordWrap(bool enabled) => WordWrapEnabled = enabled;

    public void SetTabDisplaySize(int size) => TabDisplaySize = size;

    public void SetIndentSize(int? size) => IndentSize = size;

    public void SetIndentationInputMode(IndentationInputMode mode) =>
        IndentationInputMode = mode;

    public void SetStartupPosition(int? line, int? column)
    {
        if (line is null && column is null)
        {
            return;
        }

        var snapshot = Snapshot;
        var zeroBasedLine = Math.Clamp((line ?? 1) - 1, 0, snapshot.Lines.LineCount - 1);
        var zeroBasedColumn = Math.Min(
            Math.Max(0, (column ?? 1) - 1),
            snapshot.Lines.GetLineLength(zeroBasedLine));
        var position = snapshot.Lines.GetPosition(
            new LineColumn(zeroBasedLine, zeroBasedColumn));
        SetSelection(TextSelection.Caret(position));
        LastStartupSelection = Selection;
    }
}

internal sealed class FakeStatusBarView : IStatusBarView
{
    public StatusBarState? State { get; private set; }

    public void Apply(StatusBarState state) => State = state;
}

internal sealed class FakeWindowChromeView : IWindowChromeView
{
    public bool IsStatusBarVisible { get; private set; } = true;

    public string? Title { get; private set; }

    public bool WordWrapEnabled { get; private set; }

    public int TabDisplaySize { get; private set; } = 4;

    public int? IndentSize { get; private set; }

    public IndentationInputMode IndentationInputMode { get; private set; }

    public void SetTitle(string title) => Title = title;

    public void SetStatusBarVisible(bool visible) => IsStatusBarVisible = visible;

    public void SetWordWrapLabel(bool enabled) => WordWrapEnabled = enabled;

    public void SetTabDisplaySizeLabel(int size) => TabDisplaySize = size;

    public void SetIndentSizeLabel(int? size) => IndentSize = size;

    public void SetIndentationInputModeLabel(IndentationInputMode mode) =>
        IndentationInputMode = mode;
}

internal sealed class FakeFindReplaceView : IFindReplaceView
{
    public bool IsVisible { get; private set; }

    public bool IsFindBoxFocused { get; set; }

    public string FindText { get; set; } = string.Empty;

    public string ReplaceText { get; set; } = string.Empty;

    public string? Result { get; private set; }

    public void Show(bool replace)
    {
        IsVisible = true;
        IsFindBoxFocused = true;
    }

    public void Close()
    {
        IsVisible = false;
        IsFindBoxFocused = false;
    }

    public void FocusFind() => IsFindBoxFocused = true;

    public void FocusEditor() => IsFindBoxFocused = false;

    public void SetResult(string message) => Result = message;
}

internal sealed class FakeLanguageModeMenuView : ILanguageModeMenuView
{
    private Action<string>? _onSelected;

    public IReadOnlyList<LanguageModeEntry> Entries { get; private set; } = [];

    public string? SelectedId { get; private set; }

    public void Render(
        IReadOnlyList<LanguageModeEntry> entries,
        int customModeStartIndex,
        Action<string> onSelected)
    {
        Entries = entries;
        _onSelected = onSelected;
    }

    public void Select(string id) => SelectedId = id;

    public void SelectFromMenu(string id) => _onSelected?.Invoke(id);
}

internal sealed class FakeExternalToolMenuView : IExternalToolMenuView
{
    public IReadOnlyList<ExternalToolMenuNode> Nodes { get; private set; } = [];

    public void Render(
        IReadOnlyList<ExternalToolMenuNode> nodes,
        Func<ExternalToolSettings, ExternalToolMenuState> getState,
        Func<ExternalToolSettings, Task> onSelected) => Nodes = nodes;
}

internal sealed class FakeFileDialogService : IFileDialogService
{
    public string? OpenPath { get; set; }

    public SaveFileDialogResult? SaveResult { get; set; }

    public string? ShowOpen(IReadOnlyList<FileDialogFilter> filters) => OpenPath;

    public SaveFileDialogResult? ShowSave(
        string suggestedFileName,
        TextEncodingKind defaultEncoding,
        LineEndingKind defaultLineEnding,
        IReadOnlyList<FileDialogFilter> filters,
        string defaultFilterId) => SaveResult;
}

internal sealed class FakeFileChangeMonitor : IFileChangeMonitor
{
    public event EventHandler<FileChangeDetectedEventArgs>? Changed;

    public void Trigger(string path) =>
        Changed?.Invoke(this, new FileChangeDetectedEventArgs(path));

    public void Dispose()
    {
    }
}

internal sealed class FakeFileChangeMonitorFactory : IFileChangeMonitorFactory
{
    public List<FakeFileChangeMonitor> Monitors { get; } = [];

    public IFileChangeMonitor Create(string path, bool includeSubdirectories)
    {
        var monitor = new FakeFileChangeMonitor();
        Monitors.Add(monitor);
        return monitor;
    }
}

internal sealed class FakeUiDispatcher : IUiDispatcher
{
    public bool TryEnqueue(Action action)
    {
        action();
        return true;
    }
}

internal sealed class FakeSettingsFolderOpener : ISettingsFolderOpener
{
    public string? OpenedDirectory { get; private set; }

    public Task OpenAsync(string directory, CancellationToken cancellationToken = default)
    {
        OpenedDirectory = directory;
        return Task.CompletedTask;
    }
}

internal sealed class FakeUserPrompt : IUserPrompt
{
    public PendingChangesDecision PendingDecision { get; set; } = PendingChangesDecision.Cancel;

    public ExternalChangeDecision ExternalDecision { get; set; } = ExternalChangeDecision.Reload;

    public List<(string Title, string Message)> Errors { get; } = [];

    public Task<PendingChangesDecision> ConfirmPendingChangesAsync() =>
        Task.FromResult(PendingDecision);

    public Task<ExternalChangeDecision> ResolveExternalChangeAsync() =>
        Task.FromResult(ExternalDecision);

    public Task ShowErrorAsync(string title, string message)
    {
        Errors.Add((title, message));
        return Task.CompletedTask;
    }
}

internal sealed class FakeTextFileStore : ITextFileStore
{
    public Dictionary<string, TextFileData> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<TextFileData> ReadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(Files[Path.GetFullPath(path)]);

    public Task WriteAsync(
        string path,
        string text,
        TextEncodingKind encoding,
        LineEndingKind lineEnding,
        CancellationToken cancellationToken = default)
    {
        Files[Path.GetFullPath(path)] = new TextFileData(text, encoding, lineEnding);
        return Task.CompletedTask;
    }
}
