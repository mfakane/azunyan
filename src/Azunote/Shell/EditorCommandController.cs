using Azunyan.Core;

namespace Azunote;

internal sealed class EditorCommandController
{
    private readonly IEditorView _editor;
    private readonly IWindowChromeView _window;
    private bool _wordWrapEnabled;

    public EditorCommandController(IEditorView editor, IWindowChromeView window)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public void Undo() => Execute(_editor.Undo);

    public void Redo() => Execute(_editor.Redo);

    public void Cut() => Execute(_editor.Cut);

    public void Copy() => Execute(_editor.Copy);

    public void Paste() => Execute(_editor.Paste);

    public void SelectAll() => Execute(_editor.SelectAll);

    public void ShowCompletion()
    {
        _editor.Focus();
        _editor.RequestCompletion();
    }

    public void ToggleWordWrap()
    {
        _wordWrapEnabled = !_wordWrapEnabled;
        _editor.SetWordWrap(_wordWrapEnabled);
        _window.SetWordWrapLabel(_wordWrapEnabled);
    }

    public void SetTabDisplaySize(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        _editor.SetTabDisplaySize(size);
        _window.SetTabDisplaySizeLabel(size);
    }

    public void SetIndentSize(int? size)
    {
        if (size is { } configuredSize)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(configuredSize, 1);
        }

        _editor.SetIndentSize(size);
        _window.SetIndentSizeLabel(size);
    }

    public void SetIndentationInputMode(IndentationInputMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        _editor.SetIndentationInputMode(mode);
        _window.SetIndentationInputModeLabel(mode);
    }

    public void ToggleStatusBar() =>
        _window.SetStatusBarVisible(!_window.IsStatusBarVisible);

    private void Execute(Action action)
    {
        _editor.Focus();
        action();
    }
}
