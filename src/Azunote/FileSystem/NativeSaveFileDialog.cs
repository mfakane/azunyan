using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Azunote;

public sealed record SaveFileDialogResult(
    string Path,
    TextEncoding Encoding);

/// <summary>
/// Shows the Windows common Save dialog and adds the format controls used by
/// editors such as Notepad. The dialog remains a native system dialog rather
/// than an app-owned replacement.
/// </summary>
public static class NativeSaveFileDialog
{
    private const uint EncodingGroupId = 1000;
    private const uint EncodingControlId = 1001;

    private const int HResultCancelled = unchecked((int)0x800704C7);

    public static SaveFileDialogResult? Show(
        IntPtr ownerWindowHandle,
        string suggestedFileName,
        TextEncoding defaultEncoding,
        IReadOnlyList<TextEncoding> encodings,
        IReadOnlyList<FileDialogFilter> filters,
        string defaultFilterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);
        ArgumentNullException.ThrowIfNull(filters);
        if (filters.Count == 0)
        {
            throw new ArgumentException("At least one file dialog filter is required.", nameof(filters));
        }

        IFileSaveDialog? dialog = null;
        IFileDialogCustomize? customize = null;
        IShellItem? resultItem = null;
        try
        {
            PInvoke.CoCreateInstance(
                typeof(FileSaveDialog).GUID,
                null,
                CLSCTX.CLSCTX_INPROC_SERVER,
                out dialog).ThrowOnFailure();

            var fileSaveDialog = dialog!;
            SetFileTypes(fileSaveDialog, filters);
            fileSaveDialog.SetFileTypeIndex(GetFilterIndex(filters, defaultFilterId));
            fileSaveDialog.SetDefaultExtension(GetDefaultExtension(filters, defaultFilterId));
            fileSaveDialog.SetFileName(suggestedFileName);
            fileSaveDialog.SetTitle("Save As");

            fileSaveDialog.GetOptions(out var options);
            fileSaveDialog.SetOptions(options | FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM | FILEOPENDIALOGOPTIONS.FOS_OVERWRITEPROMPT);

            customize = (IFileDialogCustomize)fileSaveDialog;
            encodings = encodings.Contains(defaultEncoding) ? encodings : [.. encodings, defaultEncoding];
            AddEncodingControl(customize, defaultEncoding, encodings);

            fileSaveDialog.Show(new HWND(ownerWindowHandle));
            fileSaveDialog.GetResult(out resultItem);
            var path = GetFileSystemPath(resultItem);
            customize.GetSelectedControlItem(EncodingControlId, out var encodingItemId);

            return new SaveFileDialogResult(path, encodings[(int)encodingItemId]);
        }
        catch (COMException exception) when (exception.HResult == HResultCancelled)
        {
            return null;
        }
        finally
        {
            ReleaseComObject(resultItem);
            ReleaseComObject(customize);
            ReleaseComObject(dialog);
        }
    }

    private static unsafe void SetFileTypes(
        IFileSaveDialog dialog,
        IReadOnlyList<FileDialogFilter> filters)
    {
        var names = filters.Select(filter => filter.DisplayName).ToArray();
        var specifications = filters.Select(filter => filter.Specification).ToArray();
        fixed (char* name = string.Join('\0', names) + '\0',
               specification = string.Join('\0', specifications) + '\0')
        {
            var fileTypes = stackalloc COMDLG_FILTERSPEC[filters.Count];
            var nameOffset = 0;
            var specificationOffset = 0;
            for (var index = 0; index < filters.Count; index++)
            {
                fileTypes[index] = new COMDLG_FILTERSPEC
                {
                    pszName = new PCWSTR(name + nameOffset),
                    pszSpec = new PCWSTR(specification + specificationOffset)
                };
                nameOffset += names[index].Length + 1;
                specificationOffset += specifications[index].Length + 1;
            }

            dialog.SetFileTypes(new ReadOnlySpan<COMDLG_FILTERSPEC>(fileTypes, filters.Count));
        }
    }

    private static uint GetFilterIndex(
        IReadOnlyList<FileDialogFilter> filters,
        string defaultFilterId)
    {
        for (var index = 0; index < filters.Count; index++)
        {
            if (string.Equals(
                filters[index].Id,
                defaultFilterId,
                StringComparison.OrdinalIgnoreCase))
            {
                return (uint)(index + 1);
            }
        }

        return 1;
    }

    private static string GetDefaultExtension(
        IReadOnlyList<FileDialogFilter> filters,
        string defaultFilterId)
    {
        var filter = filters.FirstOrDefault(item => string.Equals(
            item.Id,
            defaultFilterId,
            StringComparison.OrdinalIgnoreCase));
        var extension = filter?.Extensions.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item) && item is not "*" and not "*.*");
        return extension?.TrimStart('.').TrimStart('*') ?? "txt";
    }

    /// <summary>
    /// Places the encoding beside the Save button, as Notepad does. Only one
    /// control, or a visual group holding one, can go there; the group shows
    /// its label next to the combo box.
    /// </summary>
    private static void AddEncodingControl(
        IFileDialogCustomize customize,
        TextEncoding defaultEncoding,
        IReadOnlyList<TextEncoding> encodings)
    {
        customize.StartVisualGroup(EncodingGroupId, "Encoding:");
        customize.AddComboBox(EncodingControlId);
        for (var index = 0; index < encodings.Count; index++)
        {
            customize.AddControlItem(EncodingControlId, (uint)index, encodings[index].DisplayName);
        }

        customize.SetSelectedControlItem(EncodingControlId, (uint)encodings.TakeWhile(encoding => encoding != defaultEncoding).Count());
        customize.EndVisualGroup();
        customize.MakeProminent(EncodingGroupId);
    }

    private static unsafe string GetFileSystemPath(IShellItem shellItem)
    {
        shellItem.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out PWSTR path);
        try
        {
            return path.ToString()
                ?? throw new InvalidOperationException("The Save dialog did not return a file path.");
        }
        finally
        {
            Marshal.FreeCoTaskMem((IntPtr)path.Value);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
