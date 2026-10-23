namespace Azunote;

internal sealed class NativeFileDialogService : IFileDialogService
{
    private readonly IntPtr _ownerWindowHandle;
    private readonly Func<IReadOnlyList<TextEncoding>> _encodings;

    public NativeFileDialogService(IntPtr ownerWindowHandle, Func<IReadOnlyList<TextEncoding>> encodings)
    {
        _ownerWindowHandle = ownerWindowHandle;
        _encodings = encodings;
    }

    public string? ShowOpen(IReadOnlyList<FileDialogFilter> filters) =>
        NativeOpenFileDialog.Show(_ownerWindowHandle, filters, "supported");

    public SaveFileDialogResult? ShowSave(
        string suggestedFileName,
        TextEncoding defaultEncoding,
        IReadOnlyList<FileDialogFilter> filters,
        string defaultFilterId) =>
        NativeSaveFileDialog.Show(
            _ownerWindowHandle,
            suggestedFileName,
            defaultEncoding,
            _encodings(),
            filters,
            defaultFilterId);
}
