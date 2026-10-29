using Azunyan.Core;

namespace Azunote;

internal sealed class DocumentStatusPresenter
{
    private readonly IEditorView _editor;
    private readonly DocumentSession _session;
    private readonly IStatusBarView _statusBar;
    private readonly IWindowChromeView _window;
    private readonly Func<string> _languageModeDisplayName;
    private readonly Func<IReadOnlyList<string>> _workspacePatterns;
    private (string FilePath, IReadOnlyList<string> Patterns, (string Folder, string Name) Short)? _shortFilePath;

    public DocumentStatusPresenter(
        IEditorView editor,
        DocumentSession session,
        IStatusBarView statusBar,
        IWindowChromeView window,
        Func<string>? languageModeDisplayName = null,
        Func<IReadOnlyList<string>>? workspacePatterns = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _statusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _languageModeDisplayName = languageModeDisplayName ?? (() => "Plain Text");
        _workspacePatterns = workspacePatterns ?? (() => []);
    }

    public void Refresh(LineEndingKind? lineEnding = null)
    {
        var snapshot = _editor.Snapshot;
        var selectionStart = Math.Clamp(_editor.Selection.Start, 0, snapshot.Length);
        var lineColumn = snapshot.Lines.GetLineColumn(selectionStart);
        var currentLineEnding = lineEnding ?? _session.State.LineEnding;
        var (fileFolder, fileName) = GetShortFilePath();
        var state = new StatusBarState(
            $"Ln {lineColumn.Line + 1}, Col {lineColumn.Column + 1}",
            _session.State.Encoding.DisplayName,
            TextFileService.GetLineEndingDisplayName(currentLineEnding),
            TextEditorCommands.GetDocumentIndentationSettings(
                snapshot,
                _editor.IndentSize,
                _editor.TabDisplaySize,
                _editor.IndentationInputMode).DisplayName,
            _languageModeDisplayName(),
            _session.State.FilePath ?? "Untitled",
            _session.State.FilePath is not null,
            currentLineEnding,
            fileFolder,
            fileName);
        _statusBar.Apply(state);
    }

    /// <summary>
    /// Shortens the path for the status bar, which looks for the workspace
    /// folder only when the file or the language mode changes, not on every
    /// caret move.
    /// </summary>
    private (string Folder, string Name) GetShortFilePath()
    {
        var filePath = _session.State.FilePath;
        if (filePath is null)
        {
            return (string.Empty, "Untitled");
        }

        var patterns = _workspacePatterns();
        if (_shortFilePath is not { } cached
            || cached.FilePath != filePath
            || !cached.Patterns.SequenceEqual(patterns))
        {
            var workspaceFolder = WorkspaceFolderResolver.FindForFile(filePath, null, patterns);
            cached = (filePath, patterns, ShortenFilePath(filePath, workspaceFolder, ExternalToolContext.UserHome));
            _shortFilePath = cached;
        }

        return cached.Short;
    }

    /// <summary>
    /// Splits a path into the folder, from the workspace folder's name or
    /// with the home folder as <c>~</c>, and the file name with the separator
    /// before it, so the folder can be cut short while the name stays.
    /// </summary>
    internal static (string Folder, string Name) ShortenFilePath(
        string filePath,
        string? workspaceFolder,
        string? homeFolder)
    {
        var folder = Path.GetDirectoryName(filePath) ?? string.Empty;
        var name = Path.GetFileName(filePath);
        if (TryGetRelativeFolder(folder, workspaceFolder, out var relative))
        {
            // Named after the workspace folder, which tells apart files in
            // several of them, unless it is a whole drive.
            var root = Path.TrimEndingDirectorySeparator(workspaceFolder!);
            var rootName = Path.GetFileName(root);
            folder = rootName.Length == 0
                ? Path.Join(workspaceFolder, relative)
                : Path.Join(rootName, relative);
        }
        else if (TryGetRelativeFolder(folder, homeFolder, out relative))
        {
            folder = relative.Length == 0 ? "~" : Path.Join("~", relative);
        }

        return folder.Length == 0 || Path.EndsInDirectorySeparator(folder)
            ? (folder, name)
            : (folder, Path.DirectorySeparatorChar + name);
    }

    private static bool TryGetRelativeFolder(string folder, string? baseFolder, out string relative)
    {
        relative = string.Empty;
        if (string.IsNullOrEmpty(baseFolder))
        {
            return false;
        }

        var root = Path.TrimEndingDirectorySeparator(baseFolder);
        if (string.Equals(folder, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!folder.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        relative = folder[(root.Length + 1)..];
        return true;
    }

    public void RefreshTitle()
    {
        var name = _session.State.FilePath is null
            ? "Untitled"
            : Path.GetFileName(_session.State.FilePath);
        var dirtyMarker = _session.State.IsDirty ? " *" : string.Empty;
        var readOnlyMarker = _session.State.IsReadOnly ? " [READONLY]" : string.Empty;
        var deletedMarker = _session.State.IsDeletedOnDisk ? " [DELETED]" : string.Empty;
        _window.SetTitle(
            $"{name}{dirtyMarker}{readOnlyMarker}{deletedMarker} - {ApplicationIdentity.DisplayName}");
    }
}
