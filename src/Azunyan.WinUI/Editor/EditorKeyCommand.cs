namespace Azunyan.WinUI;

/// <summary>
/// A host command claimed through <see cref="AzunyanEditorView.HostKeyCommand"/>.
/// A non-repeatable command runs once until its key is released.
/// </summary>
public readonly record struct EditorKeyCommand(Action Execute, bool Repeatable);
