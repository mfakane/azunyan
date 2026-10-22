using Azunyan.Core;

namespace Azunote;

/// <summary>
/// File-level state for the one document displayed by an Azunote window.
/// The editable text itself remains owned by <see cref="Document"/> through
/// the editor control; this class only owns its file identity and saved
/// representation.
/// </summary>
public sealed class DocumentSession
{
    private string _savedText = string.Empty;

    /// <summary>
    /// The user chose an encoding or line ending that is not saved yet. A
    /// change taken from .editorconfig is not counted: it applies on the next
    /// save without making the document dirty.
    /// </summary>
    private bool _formatChanged;
    private DocumentSessionState _state = new(
        FilePath: null,
        Encoding: TextEncodingKind.Utf8,
        LineEnding: GetDefaultLineEnding(),
        IsDirty: false);

    public DocumentSessionState State => _state;

    /// <summary>
    /// Names this document's backup. A restored document keeps the id of the
    /// backup it came from, so that backup is updated rather than duplicated.
    /// </summary>
    public string BackupId { get; internal set; } = Guid.NewGuid().ToString("N");

    public event EventHandler? StateChanged;

    public bool IsSameAsSaved(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var measurement = ShellPerformance.Measure("document.compare");
        if (ReferenceEquals(text, _savedText)) return true;
        if (text.IndexOf('\r') < 0) return string.Equals(text, _savedText, StringComparison.Ordinal);
        // The saved representation is already normalized. Compare the current
        // representation without allocating another document-sized string.
        var savedIndex = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n') index++;
                character = '\n';
            }
            if (savedIndex >= _savedText.Length || character != _savedText[savedIndex++]) return false;
        }
        return savedIndex == _savedText.Length;
    }

    public void ObserveText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Update(_state with { IsDirty = IsDirtyWith(text) });
    }

    /// <summary>
    /// Changes how the document is written on its next save. The document is
    /// dirty until then, so the choice is not lost on close.
    /// </summary>
    public void ChangeFormat(TextEncodingKind? encoding, LineEndingKind? lineEnding)
    {
        var state = _state with
        {
            Encoding = encoding ?? _state.Encoding,
            LineEnding = lineEnding is { } kind ? GetLineEndingOrDefault(kind) : _state.LineEnding
        };
        if (state == _state)
        {
            return;
        }

        _formatChanged = true;
        Update(state with { IsDirty = true });
    }

    /// <summary>
    /// The file was removed from disk. The document stays open with its path
    /// and counts as unsaved, so closing it asks or keeps a backup.
    /// </summary>
    public void MarkDeletedOnDisk()
    {
        if (_state.FilePath is null)
        {
            return;
        }

        Update(_state with { IsDeletedOnDisk = true, IsDirty = true });
    }

    /// <summary>The file exists on disk again.</summary>
    public void MarkPresentOnDisk(string currentText)
    {
        ArgumentNullException.ThrowIfNull(currentText);
        if (!_state.IsDeletedOnDisk)
        {
            return;
        }

        Update(_state with
        {
            IsDeletedOnDisk = false,
            IsDirty = _formatChanged || !IsSameAsSaved(currentText)
        });
    }

    private bool IsDirtyWith(string text) =>
        _formatChanged || _state.IsDeletedOnDisk || !IsSameAsSaved(text);

    public void SetUntitled(
        string currentText,
        TextEncodingKind encoding,
        LineEndingKind lineEnding)
    {
        ArgumentNullException.ThrowIfNull(currentText);
        _savedText = string.Empty;
        _formatChanged = false;
        Update(new DocumentSessionState(
            FilePath: null,
            Encoding: encoding,
            LineEnding: GetLineEndingOrDefault(lineEnding),
            IsDirty: !string.IsNullOrEmpty(currentText)));
    }

    /// <summary>
    /// Makes the document text to read rather than to save: read-only, and
    /// with no unsaved changes, so closing it neither asks nor keeps a backup.
    /// </summary>
    public void MarkReadOnly(string currentText)
    {
        ArgumentNullException.ThrowIfNull(currentText);
        _savedText = NormalizeLineEndings(currentText);
        _formatChanged = false;
        Update(_state with { IsDirty = false, IsReadOnly = true });
    }

    public void Load(string fullPath, TextFileData data, bool isReadOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentNullException.ThrowIfNull(data);
        SetSavedState(fullPath, data, isReadOnly);
    }

    public void MarkSaved(
        string fullPath,
        string currentText,
        TextEncodingKind encoding,
        LineEndingKind lineEnding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentNullException.ThrowIfNull(currentText);
        _savedText = NormalizeLineEndings(currentText);
        _formatChanged = false;
        Update(new DocumentSessionState(
            FilePath: Path.GetFullPath(fullPath),
            Encoding: encoding,
            LineEnding: GetLineEndingOrDefault(lineEnding),
            IsDirty: false));
    }

    public void ApplyDiskReload(string fullPath, TextFileData data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentNullException.ThrowIfNull(data);
        SetSavedState(fullPath, data, _state.IsReadOnly);
    }

    /// <summary>
    /// Applies the output of a tool's temporary file. The saved disk text is
    /// intentionally unchanged, so a previously dirty document remains dirty.
    /// </summary>
    public void ApplyTemporaryReload(TextFileData data, string currentText)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(currentText);
        Update(_state with
        {
            Encoding = data.Encoding,
            LineEnding = GetLineEndingOrDefault(data.LineEnding),
            IsDirty = IsDirtyWith(currentText)
        });
    }

    public void ApplyEditorConfig(EditorConfigSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Update(_state with
        {
            Encoding = settings.Encoding ?? _state.Encoding,
            LineEnding = settings.LineEnding is { } lineEnding
                ? GetLineEndingOrDefault(lineEnding)
                : _state.LineEnding
        });
    }

    public static LineEndingKind GetDefaultLineEnding() =>
        OperatingSystem.IsWindows() ? LineEndingKind.CrLf : LineEndingKind.Lf;

    public static LineEndingKind GetLineEndingOrDefault(LineEndingKind lineEnding) =>
        lineEnding is LineEndingKind.CrLf or LineEndingKind.Lf or LineEndingKind.Cr
            ? lineEnding
            : GetDefaultLineEnding();

    private void SetSavedState(string fullPath, TextFileData data, bool isReadOnly)
    {
        _savedText = NormalizeLineEndings(data.Text);
        _formatChanged = false;
        Update(new DocumentSessionState(
            FilePath: Path.GetFullPath(fullPath),
            Encoding: data.Encoding,
            LineEnding: GetLineEndingOrDefault(data.LineEnding),
            IsDirty: false,
            IsReadOnly: isReadOnly));
    }

    private void Update(DocumentSessionState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string NormalizeLineEndings(string text)
    {
        if (text.IndexOf('\r') < 0)
        {
            return text;
        }

        var normalized = new System.Text.StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                normalized.Append('\n');
            }
            else
            {
                normalized.Append(text[index]);
            }
        }

        return normalized.ToString();
    }
}

public sealed record DocumentSessionState(
    string? FilePath,
    TextEncodingKind Encoding,
    LineEndingKind LineEnding,
    bool IsDirty,
    bool IsReadOnly = false,
    bool IsDeletedOnDisk = false);
