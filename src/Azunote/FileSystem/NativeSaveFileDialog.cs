using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Azunote;

public sealed record SaveFileDialogResult(
    string Path,
    TextEncodingKind Encoding,
    LineEndingKind LineEnding);

/// <summary>
/// Shows the Windows common Save dialog and adds the format controls used by
/// editors such as Notepad. The dialog remains a native system dialog rather
/// than an app-owned replacement.
/// </summary>
public static class NativeSaveFileDialog
{
    private const uint EncodingControlId = 1001;
    private const uint LineEndingControlId = 1002;

    private const uint Utf8ItemId = 1;
    private const uint Utf8BomItemId = 2;
    private const uint Utf16LittleEndianItemId = 3;
    private const uint Utf16BigEndianItemId = 4;
    private const uint SystemDefaultItemId = 5;

    private const uint CrLfItemId = 11;
    private const uint LfItemId = 12;
    private const uint CrItemId = 13;

    private const int HResultCancelled = unchecked((int)0x800704C7);

    public static SaveFileDialogResult? Show(
        IntPtr ownerWindowHandle,
        string suggestedFileName,
        TextEncodingKind defaultEncoding,
        LineEndingKind defaultLineEnding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);

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
            SetFileTypes(fileSaveDialog);
            fileSaveDialog.SetFileTypeIndex(1);
            fileSaveDialog.SetDefaultExtension("txt");
            fileSaveDialog.SetFileName(suggestedFileName);
            fileSaveDialog.SetTitle("Save As");

            fileSaveDialog.GetOptions(out var options);
            fileSaveDialog.SetOptions(options | FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM | FILEOPENDIALOGOPTIONS.FOS_OVERWRITEPROMPT);

            customize = (IFileDialogCustomize)fileSaveDialog;
            AddFormatControls(customize, defaultEncoding, defaultLineEnding);

            fileSaveDialog.Show(new HWND(ownerWindowHandle));
            fileSaveDialog.GetResult(out resultItem);
            var path = GetFileSystemPath(resultItem);
            customize.GetSelectedControlItem(EncodingControlId, out var encodingItemId);
            customize.GetSelectedControlItem(LineEndingControlId, out var lineEndingItemId);

            return new SaveFileDialogResult(
                path,
                GetEncoding(encodingItemId),
                GetLineEnding(lineEndingItemId));
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

    private static unsafe void SetFileTypes(IFileSaveDialog dialog)
    {
        fixed (
            char* textName = "Text files",
            textSpecification = "*.txt;*.md;*.log;*.json;*.xml;*.csv",
            allName = "All files",
            allSpecification = "*.*")
        {
            var fileTypes = stackalloc COMDLG_FILTERSPEC[2];
            fileTypes[0] = new COMDLG_FILTERSPEC
            {
                pszName = new PCWSTR(textName),
                pszSpec = new PCWSTR(textSpecification)
            };
            fileTypes[1] = new COMDLG_FILTERSPEC
            {
                pszName = new PCWSTR(allName),
                pszSpec = new PCWSTR(allSpecification)
            };

            dialog.SetFileTypes(new ReadOnlySpan<COMDLG_FILTERSPEC>(fileTypes, 2));
        }
    }

    private static void AddFormatControls(
        IFileDialogCustomize customize,
        TextEncodingKind defaultEncoding,
        LineEndingKind defaultLineEnding)
    {
        customize.AddComboBox(EncodingControlId);
        customize.SetControlLabel(EncodingControlId, "Encoding");
        customize.AddControlItem(EncodingControlId, Utf8ItemId, "UTF-8");
        customize.AddControlItem(EncodingControlId, Utf8BomItemId, "UTF-8 BOM");
        customize.AddControlItem(EncodingControlId, Utf16LittleEndianItemId, "UTF-16 LE");
        customize.AddControlItem(EncodingControlId, Utf16BigEndianItemId, "UTF-16 BE");
        customize.AddControlItem(EncodingControlId, SystemDefaultItemId, "System default");
        customize.SetSelectedControlItem(EncodingControlId, GetEncodingItemId(defaultEncoding));

        customize.AddComboBox(LineEndingControlId);
        customize.SetControlLabel(LineEndingControlId, "Line ending");
        customize.AddControlItem(LineEndingControlId, CrLfItemId, "Windows (CRLF)");
        customize.AddControlItem(LineEndingControlId, LfItemId, "Unix (LF)");
        customize.AddControlItem(LineEndingControlId, CrItemId, "Classic Mac (CR)");
        customize.SetSelectedControlItem(LineEndingControlId, GetLineEndingItemId(defaultLineEnding));
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

    private static uint GetEncodingItemId(TextEncodingKind encoding) => encoding switch
    {
        TextEncodingKind.Utf8 => Utf8ItemId,
        TextEncodingKind.Utf8Bom => Utf8BomItemId,
        TextEncodingKind.Utf16LittleEndian => Utf16LittleEndianItemId,
        TextEncodingKind.Utf16BigEndian => Utf16BigEndianItemId,
        TextEncodingKind.SystemDefault => SystemDefaultItemId,
        _ => Utf8ItemId
    };

    private static TextEncodingKind GetEncoding(uint itemId) => itemId switch
    {
        Utf8ItemId => TextEncodingKind.Utf8,
        Utf8BomItemId => TextEncodingKind.Utf8Bom,
        Utf16LittleEndianItemId => TextEncodingKind.Utf16LittleEndian,
        Utf16BigEndianItemId => TextEncodingKind.Utf16BigEndian,
        SystemDefaultItemId => TextEncodingKind.SystemDefault,
        _ => throw new InvalidOperationException("The Save dialog returned an unknown encoding.")
    };

    private static uint GetLineEndingItemId(LineEndingKind lineEnding) => lineEnding switch
    {
        LineEndingKind.CrLf => CrLfItemId,
        LineEndingKind.Lf => LfItemId,
        LineEndingKind.Cr => CrItemId,
        _ => OperatingSystem.IsWindows() ? CrLfItemId : LfItemId
    };

    private static LineEndingKind GetLineEnding(uint itemId) => itemId switch
    {
        CrLfItemId => LineEndingKind.CrLf,
        LfItemId => LineEndingKind.Lf,
        CrItemId => LineEndingKind.Cr,
        _ => throw new InvalidOperationException("The Save dialog returned an unknown line ending.")
    };

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
