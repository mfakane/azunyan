using Azunyan.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Azunyan.WinUI;

public sealed partial class AzunyanEditorView
{
    private TaskCompletionSource<string?>? _inlinePromptSubmission;
    private DocumentAnchor? _inlineChatAnchor;

    /// <summary>
    /// Opens the inline chat at the current caret and completes when the user
    /// submits one message or cancels the prompt.
    /// </summary>
    public Task<string?> PromptInlineChatAsync(string title, string placeholder)
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
        InlinePromptChatPanel.PreparePrompt(
            title,
            placeholder,
            clearInput: !InlinePromptPopup.IsOpen);
        _inlinePromptSubmission = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        SetInlineChatProcessing(isProcessing: false);

        InlinePromptPopup.IsOpen = true;
        UpdateInlineChatPopupPosition();
        DispatcherQueue.TryEnqueue(InlinePromptChatPanel.FocusInput);
        return _inlinePromptSubmission.Task;
    }

    /// <summary>Appends a completed external-tool response to the inline chat.</summary>
    public void AppendInlineChatResponse(string response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => AppendInlineChatResponse(response));
            return;
        }

        PrepareInlineChatResponseDisplay();
        InlinePromptChatPanel.AppendEntry(
            false,
            response,
            canInsert: !IsReadOnly);
        UpdateInlineChatPopupPosition();
    }

    public void BeginInlineChatResponse(Guid responseId)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => BeginInlineChatResponse(responseId));
            return;
        }

        PrepareInlineChatResponseDisplay();
        InlinePromptChatPanel.BeginStreamingEntry(responseId, canInsert: !IsReadOnly);
        UpdateInlineChatPopupPosition();
    }

    public void AppendInlineChatResponseChunk(Guid responseId, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => AppendInlineChatResponseChunk(responseId, text));
            return;
        }

        InlinePromptChatPanel.AppendStreamingText(responseId, text);
        InlinePromptChatPanel.UpdateLayout();
        UpdateInlineChatPopupPosition();
    }

    public void CompleteInlineChatResponse(Guid responseId)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => CompleteInlineChatResponse(responseId));
            return;
        }

        InlinePromptChatPanel.FinishStreamingEntry(responseId);
    }

    private void PrepareInlineChatResponseDisplay()
    {
        _inlineChatAnchor ??= Document.CaretSet.Primary.CaretAnchor;
        InlinePromptPopup.IsOpen = true;
        InlinePromptChatPanel.SetDismissButtonContent(
            _inlinePromptSubmission is null ? "Close" : "Cancel");
    }

    /// <summary>Updates the inline chat send button while an external tool runs.</summary>
    public void SetInlineChatProcessing(bool isProcessing)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => SetInlineChatProcessing(isProcessing));
            return;
        }

        InlinePromptChatPanel.SetProcessing(
            isProcessing,
            canSend: _inlinePromptSubmission is not null);
    }

    private void SubmitInlinePrompt()
    {
        if (_inlinePromptSubmission is not { } submission)
        {
            return;
        }

        var text = InlinePromptChatPanel.InputText;
        InlinePromptChatPanel.AppendEntry(true, text, canInsert: !IsReadOnly);
        InlinePromptChatPanel.InputText = string.Empty;
        InlinePromptChatPanel.SetDismissButtonContent("Close");
        _inlinePromptSubmission = null;
        SetInlineChatProcessing(isProcessing: true);
        submission.TrySetResult(text);
    }

    private void DismissInlineChat()
    {
        var pendingSubmission = _inlinePromptSubmission;
        _inlinePromptSubmission = null;
        _inlineChatAnchor = null;
        InlinePromptPopup.IsOpen = false;
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
