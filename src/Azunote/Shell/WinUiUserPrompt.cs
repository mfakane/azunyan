using Azunyan.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Azunote;

internal sealed class WinUiUserPrompt : IUserPrompt
{
    private readonly Func<XamlRoot?> _xamlRoot;
    private readonly Func<string, string, string, bool, Task<ExternalToolPromptInput?>> _promptInlineChat;

    public WinUiUserPrompt(
        Func<XamlRoot?> xamlRoot,
        Func<string, string, string, bool, Task<ExternalToolPromptInput?>> promptInlineChat)
    {
        _xamlRoot = xamlRoot ?? throw new ArgumentNullException(nameof(xamlRoot));
        _promptInlineChat = promptInlineChat
            ?? throw new ArgumentNullException(nameof(promptInlineChat));
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

    public Task<ExternalToolPromptInput?> PromptExternalToolInputAsync(
        string conversationKey,
        string title,
        string placeholder,
        bool preserveChatHistory) =>
        _promptInlineChat(conversationKey, title, placeholder, preserveChatHistory);

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
