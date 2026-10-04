using Azunyan.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Azunyan.WinUI;

public sealed partial class AzunyanEditorView
{
    private TaskCompletionSource<ExternalToolPromptInput?>? _inlinePromptSubmission;
    private DocumentAnchor? _inlineChatAnchor;

    /// <summary>
    /// Opens the inline chat at the current caret and completes when the user
    /// submits one message or cancels the prompt.
    /// </summary>
    public Task<ExternalToolPromptInput?> PromptInlineChatAsync(
        string conversationKey,
        string title,
        string placeholder,
        bool preserveChatHistory)
    {
        ArgumentNullException.ThrowIfNull(placeholder);
        if (_inlinePromptSubmission is not null)
        {
            throw new InvalidOperationException(
                "An inline chat prompt is already waiting for input.");
        }

        var primaryCaret = Document.CaretSet.Primary;
        _inlineChatAnchor = primaryCaret.Selection.IsEmpty
            ? primaryCaret.CaretAnchor
            : DocumentAnchor.After(primaryCaret.Selection.End);
        var sameConversation = InlinePromptChatPanel.IsActiveConversation(conversationKey);
        InlinePromptChatPanel.PreparePrompt(
            conversationKey,
            title,
            placeholder,
            preserveChatHistory,
            clearInput: !InlinePromptPopup.IsOpen || !sameConversation);
        _inlinePromptSubmission = new TaskCompletionSource<ExternalToolPromptInput?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        SetInlineChatProcessing(conversationKey, isProcessing: false);

        InlinePromptPopup.IsOpen = true;
        UpdateInlineChatPopupPosition();
        DispatcherQueue.TryEnqueue(InlinePromptChatPanel.FocusInput);
        return _inlinePromptSubmission.Task;
    }

    /// <summary>Appends a completed external-tool response to the inline chat.</summary>
    public void AppendInlineChatResponse(string conversationKey, string response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => AppendInlineChatResponse(conversationKey, response));
            return;
        }

        var shouldDisplay = PrepareInlineChatResponseDisplay(conversationKey);
        InlinePromptChatPanel.AppendEntry(
            conversationKey,
            false,
            response,
            canInsert: !IsReadOnly);
        if (shouldDisplay)
        {
            UpdateInlineChatPopupPosition();
        }
    }

    public void BeginInlineChatResponse(string conversationKey, Guid responseId)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => BeginInlineChatResponse(conversationKey, responseId));
            return;
        }

        var shouldDisplay = PrepareInlineChatResponseDisplay(conversationKey);
        InlinePromptChatPanel.BeginStreamingEntry(
            conversationKey,
            responseId,
            canInsert: !IsReadOnly);
        if (shouldDisplay)
        {
            UpdateInlineChatPopupPosition();
        }
    }

    public void AppendInlineChatResponseChunk(
        string conversationKey,
        Guid responseId,
        string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => AppendInlineChatResponseChunk(
                conversationKey,
                responseId,
                text));
            return;
        }

        InlinePromptChatPanel.AppendStreamingText(conversationKey, responseId, text);
        if (InlinePromptChatPanel.IsActiveConversation(conversationKey)
            && InlinePromptPopup.IsOpen)
        {
            InlinePromptChatPanel.UpdateLayout();
            UpdateInlineChatPopupPosition();
        }
    }

    public void CompleteInlineChatResponse(string conversationKey, Guid responseId)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => CompleteInlineChatResponse(conversationKey, responseId));
            return;
        }

        InlinePromptChatPanel.FinishStreamingEntry(conversationKey, responseId);
    }

    private bool PrepareInlineChatResponseDisplay(string conversationKey)
    {
        if (!InlinePromptPopup.IsOpen)
        {
            InlinePromptChatPanel.ActivateConversation(conversationKey);
        }

        if (!InlinePromptChatPanel.IsActiveConversation(conversationKey))
        {
            return false;
        }

        _inlineChatAnchor ??= Document.CaretSet.Primary.CaretAnchor;
        InlinePromptPopup.IsOpen = true;
        InlinePromptChatPanel.SetDismissButtonContent(
            _inlinePromptSubmission is null ? "Close" : "Cancel");
        return true;
    }

    /// <summary>Updates the inline chat send button while an external tool runs.</summary>
    public void SetInlineChatProcessing(string conversationKey, bool isProcessing)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => SetInlineChatProcessing(
                conversationKey,
                isProcessing));
            return;
        }

        InlinePromptChatPanel.SetProcessing(
            conversationKey,
            isProcessing,
            canSend: _inlinePromptSubmission is not null
                && InlinePromptChatPanel.IsActiveConversation(conversationKey));
    }

    private void SubmitInlinePrompt()
    {
        if (_inlinePromptSubmission is not { } submission)
        {
            return;
        }

        var conversationKey = InlinePromptChatPanel.ActiveConversationKey;
        if (conversationKey is null)
        {
            return;
        }

        var text = InlinePromptChatPanel.InputText;
        var promptInput = InlinePromptChatPanel.AppendUserEntryAndCreatePromptInput(
            conversationKey,
            text,
            canInsert: !IsReadOnly);
        InlinePromptChatPanel.InputText = string.Empty;
        InlinePromptChatPanel.SetDismissButtonContent("Close");
        _inlinePromptSubmission = null;
        InlinePromptChatPanel.SetProcessing(
            conversationKey,
            isProcessing: true,
            canSend: false);
        submission.TrySetResult(promptInput);
    }

    private void DismissInlineChat()
    {
        var pendingSubmission = _inlinePromptSubmission;
        _inlinePromptSubmission = null;
        _inlineChatAnchor = null;
        InlinePromptPopup.IsOpen = false;
        InlinePromptChatPanel.CloseConversation();
        InlinePromptChatPanel.FinishPrompt();
        pendingSubmission?.TrySetResult(null);
    }

    private void InsertInlineChatEntry(string text)
    {
        if (IsReadOnly)
        {
            return;
        }

        ReplaceDocumentRange(Document.Selection.Range, text);
        Focus(FocusState.Programmatic);
        ScrollSelectionIntoView();
    }

    private void UpdateInlineChatPopupPosition()
    {
        if (!InlinePromptPopup.IsOpen)
        {
            return;
        }

        var anchor = _inlineChatAnchor ?? Document.CaretSet.Primary.CaretAnchor;
        var inputOrigin = ProjectedSurfaceHost.TransformToVisual(RootGrid)
            .TransformPoint(new Point(0, 0));
        if (!TryGetRendererCaretRect(anchor, out var caretRect))
        {
            InlinePromptPopup.HorizontalOffset = Math.Max(8, inputOrigin.X + 8);
            InlinePromptPopup.VerticalOffset = Math.Max(8, inputOrigin.Y + 8);
            InlinePromptChatPanel.SetCalloutTail(
                pointAbove: false,
                showTail: false,
                horizontalOffset: 0);
            return;
        }

        var availableWidth = Math.Min(
            ProjectedSurfaceHost.ActualWidth,
            RootGrid.ActualWidth);
        InlinePromptChatPanel.BubbleWidth = Math.Max(
            1,
            Math.Min(680, availableWidth - 16));

        var anchorX = inputOrigin.X + caretRect.X;
        var maxX = Math.Max(8, RootGrid.ActualWidth - InlinePromptChatPanel.BubbleWidth - 8);
        var x = Math.Clamp(anchorX - 24, 8, maxX);
        InlinePromptPopup.HorizontalOffset = x;

        var tailOffset = Math.Clamp(
            anchorX - x - 9,
            12,
            Math.Max(12, InlinePromptChatPanel.BubbleWidth - 30));
        var belowY = inputOrigin.Y + caretRect.Y + caretRect.Height + 2;
        var bubbleHeight = InlinePromptChatPanel.BubbleHeight;
        var pointAbove = bubbleHeight > 0
            && belowY + bubbleHeight > RootGrid.ActualHeight - 8;
        var y = pointAbove
            ? inputOrigin.Y + caretRect.Y - bubbleHeight - 2
            : belowY;
        if (pointAbove && y < 8)
        {
            pointAbove = false;
            y = belowY;
        }

        InlinePromptChatPanel.SetCalloutTail(
            pointAbove,
            showTail: true,
            tailOffset);
        InlinePromptPopup.VerticalOffset = Math.Max(8, y);
    }
}
