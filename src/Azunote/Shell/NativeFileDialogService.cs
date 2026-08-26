namespace Azunote;

internal sealed class NativeFileDialogService : IFileDialogService
{
    private readonly IntPtr _ownerWindowHandle;

    public NativeFileDialogService(IntPtr ownerWindowHandle)
    {
        _ownerWindowHandle = ownerWindowHandle;
    }

    public string? ShowOpen(IReadOnlyList<FileDialogFilter> filters) =>
        NativeOpenFileDialog.Show(_ownerWindowHandle, filters, "supported");

    public SaveFileDialogResult? ShowSave(
        string suggestedFileName,
        TextEncodingKind defaultEncoding,
        LineEndingKind defaultLineEnding,
        IReadOnlyList<FileDialogFilter> filters,
        string defaultFilterId) =>
        NativeSaveFileDialog.Show(
            _ownerWindowHandle,
            suggestedFileName,
            defaultEncoding,
            defaultLineEnding,
            filters,
            defaultFilterId);
}
