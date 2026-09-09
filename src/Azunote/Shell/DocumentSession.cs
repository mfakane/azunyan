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
    private DocumentSessionState _state = new(
        FilePath: null,
        Encoding: TextEncodingKind.Utf8,
        LineEnding: GetDefaultLineEnding(),
        IsDirty: false);

    public DocumentSessionState State => _state;

    public event EventHandler? StateChanged;

    public bool IsSameAsSaved(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.Equals(
            NormalizeLineEndings(text),
            NormalizeLineEndings(_savedText),
            StringComparison.Ordinal);
    }

    public void ObserveText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Update(_state with { IsDirty = !IsSameAsSaved(text) });
    }

    public void SetUntitled(
        string currentText,
        TextEncodingKind encoding,
        LineEndingKind lineEnding)
    {
        ArgumentNullException.ThrowIfNull(currentText);
        _savedText = string.Empty;
        Update(new DocumentSessionState(
            FilePath: null,
            Encoding: encoding,
            LineEnding: GetLineEndingOrDefault(lineEnding),
            IsDirty: !string.IsNullOrEmpty(currentText)));
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
        _savedText = currentText;
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
            IsDirty = !IsSameAsSaved(currentText)
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
        _savedText = data.Text;
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
    bool IsReadOnly = false);
