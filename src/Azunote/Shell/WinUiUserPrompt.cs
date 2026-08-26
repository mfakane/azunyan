using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Azunote;

internal sealed class WinUiUserPrompt : IUserPrompt
{
    private readonly Func<XamlRoot?> _xamlRoot;

    public WinUiUserPrompt(Func<XamlRoot?> xamlRoot)
    {
        _xamlRoot = xamlRoot ?? throw new ArgumentNullException(nameof(xamlRoot));
    }

    public async Task<PendingChangesDecision> ConfirmPendingChangesAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Save changes?",
            Content = "The current document has unsaved changes.",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Don't save",
            CloseButtonText = "Cancel",
            XamlRoot = _xamlRoot()
        };

        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => PendingChangesDecision.Save,
            ContentDialogResult.Secondary => PendingChangesDecision.Discard,
            _ => PendingChangesDecision.Cancel
        };
    }

    public async Task<ExternalChangeDecision> ResolveExternalChangeAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "File changed externally",
            Content = "The file changed outside Azunote while this document has unsaved changes.",
            PrimaryButtonText = "Reload file",
            SecondaryButtonText = "Keep my changes",
            CloseButtonText = "Cancel",
            XamlRoot = _xamlRoot()
        };

        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => ExternalChangeDecision.Reload,
            ContentDialogResult.Secondary => ExternalChangeDecision.Keep,
            _ => ExternalChangeDecision.Cancel
        };
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        try
        {
            var root = _xamlRoot();
            if (root is null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = root
            };
            await dialog.ShowAsync();
        }
        catch (Exception exception)
        {
            ErrorReporter.LogException("Error dialog failure", exception);
        }
    }
}
