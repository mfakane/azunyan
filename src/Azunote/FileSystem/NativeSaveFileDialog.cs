using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Azunote;

/// <summary>
/// Shows the Windows common Save dialog. The encoding is chosen from the
/// File menu instead, so the dialog stays as the system shows it.
/// </summary>
public static class NativeSaveFileDialog
{
    private const int HResultCancelled = unchecked((int)0x800704C7);

    public static string? Show(
        IntPtr ownerWindowHandle,
        string suggestedFileName,
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

            fileSaveDialog.Show(new HWND(ownerWindowHandle));
            fileSaveDialog.GetResult(out resultItem);
            return GetFileSystemPath(resultItem);
        }
        catch (COMException exception) when (exception.HResult == HResultCancelled)
        {
            return null;
        }
        finally
        {
            ReleaseComObject(resultItem);
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
