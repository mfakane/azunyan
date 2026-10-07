using Azunyan.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Azunyan.WinUI;

public sealed partial class AzunyanEditorView
{
    private const double InlineChatShadowClearance = 16;
    private const double InlineChatViewportPadding = 8;
    private const double InlineChatTailCenterFromLeft = 24;
    private TaskCompletionSource<ExternalToolPromptInput?>? _inlinePromptSubmission;
    private bool? _inlineChatPointAbove;
    private double? _inlineChatBubbleX;
    private TextSnapshot? _inlineChatRenderedSnapshot;
    private DocumentAnchor _inlineChatRenderedCaretAnchor;
    private bool _inlineChatHasRenderedCaret;
    private bool _inlineChatPositionUpdateQueued;

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

        _inlineChatPointAbove = null;
        _inlineChatBubbleX = null;
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
        QueueInlineChatPopupPositionUpdate();
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
            QueueInlineChatPopupPositionUpdate();
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
            QueueInlineChatPopupPositionUpdate();
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
            QueueInlineChatPopupPositionUpdate();
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
            _inlineChatPointAbove = null;
            _inlineChatBubbleX = null;
        }

        if (!InlinePromptChatPanel.IsActiveConversation(conversationKey))
        {
            return false;
        }

        InlinePromptPopup.IsOpen = true;
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
        _inlineChatPointAbove = null;
        _inlineChatBubbleX = null;
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

    private void QueueInlineChatPopupPositionUpdate()
    {
        if (!InlinePromptPopup.IsOpen || _inlineChatPositionUpdateQueued)
        {
            return;
        }

        _inlineChatPositionUpdateQueued = true;
        CompositionTarget.Rendering += OnInlineChatRendering;
    }

    private void OnInlineChatRendering(object? sender, object args)
    {
        CompositionTarget.Rendering -= OnInlineChatRendering;
        try
        {
            UpdateInlineChatPopupPosition();
        }
        finally
        {
            _inlineChatPositionUpdateQueued = false;
        }
    }

    private void UpdateInlineChatPopupPosition()
    {
        if (!InlinePromptPopup.IsOpen)
        {
            return;
        }

        var anchor = Document.CaretSet.Primary.CaretAnchor;
        if (_renderer is not null
            && (!_inlineChatHasRenderedCaret
                || !ReferenceEquals(_inlineChatRenderedSnapshot, Snapshot)
                || !_inlineChatRenderedCaretAnchor.Equals(anchor)))
        {
            // A document change can queue this before its renderer pass. Keep
            // the last aligned position until the matching caret geometry exists.
            return;
        }

        var rasterizationScale = XamlRoot?.RasterizationScale ?? 1;
        if (!double.IsFinite(rasterizationScale) || rasterizationScale <= 0)
        {
            rasterizationScale = 1;
        }

        var inputOrigin = ProjectedSurfaceHost.TransformToVisual(EditorHost)
            .TransformPoint(new Point(0, 0));
        if (!TryGetRendererCaretRect(anchor, out var caretRect))
        {
            _inlineChatBubbleX = null;
            SetPopupOffset(
                horizontal: true,
                inputOrigin.X + InlineChatViewportPadding,
                rasterizationScale);
            SetPopupOffset(
                horizontal: false,
                inputOrigin.Y + InlineChatViewportPadding,
                rasterizationScale);
            InlinePromptChatPanel.SetCalloutTail(
                pointAbove: false,
                showTail: false,
                horizontalOffset: 0);
            return;
        }

        var horizontalInset = InlineChatShadowClearance + InlineChatViewportPadding;
        var availableWidth = Math.Min(
            ProjectedSurfaceHost.ActualWidth,
            EditorHost.ActualWidth) - horizontalInset * 2;
        var bubbleWidth = Math.Max(1, Math.Min(680, availableWidth));
        if (!double.IsFinite(InlinePromptChatPanel.BubbleWidth)
            || Math.Abs(InlinePromptChatPanel.BubbleWidth - bubbleWidth) > 0.5)
        {
            InlinePromptChatPanel.BubbleWidth = bubbleWidth;
            InlinePromptChatPanel.UpdateLayout();
        }

        var anchorX = inputOrigin.X + caretRect.X;
        var minX = -GutterColumn.ActualWidth + horizontalInset;
        var maxX = Math.Max(
            minX,
            EditorHost.ActualWidth - InlinePromptChatPanel.BubbleWidth - horizontalInset);
        var maxTailOffset = Math.Max(0, InlinePromptChatPanel.BubbleWidth - 18);
        var minXForTail = anchorX - maxTailOffset - 9;
        var maxXForTail = anchorX - 9;
        var tailCanStayAligned = minXForTail <= maxX && maxXForTail >= minX;
        var minAlignedX = tailCanStayAligned
            ? Math.Max(minX, minXForTail)
            : minX;
        var maxAlignedX = tailCanStayAligned
            ? Math.Min(maxX, maxXForTail)
            : maxX;
        var previousX = _inlineChatBubbleX ?? (anchorX - InlineChatTailCenterFromLeft);
        var x = SnapToPhysicalPixel(
            Math.Clamp(previousX, minAlignedX, maxAlignedX),
            rasterizationScale);
        _inlineChatBubbleX = x;
        SetPopupOffset(horizontal: true, x, rasterizationScale);

        var tailOffset = SnapToPhysicalPixel(
            Math.Clamp(
                anchorX - x - 9,
                0,
                Math.Max(0, InlinePromptChatPanel.BubbleWidth - 18)),
            rasterizationScale);
        var belowY = inputOrigin.Y + caretRect.Y + caretRect.Height + 2;
        var bubbleHeight = InlinePromptChatPanel.BubbleHeight;
        var aboveY = inputOrigin.Y + caretRect.Y - bubbleHeight - 2;
        var safeTop = InlineChatShadowClearance + InlineChatViewportPadding;
        var safeBottom = EditorHost.ActualHeight - safeTop;
        var canFitAbove = aboveY >= safeTop;
        var canFitBelow = belowY + bubbleHeight <= safeBottom;
        // Keep the current side until it stops fitting so a growing input does not
        // make the callout flip back and forth at the viewport boundary.
        var pointAbove = _inlineChatPointAbove switch
        {
            true when canFitAbove => true,
            false when canFitBelow => false,
            _ => !canFitBelow && canFitAbove
        };
        if (!canFitAbove && !canFitBelow)
        {
            var spaceAbove = inputOrigin.Y + caretRect.Y - safeTop;
            var spaceBelow = safeBottom - belowY;
            pointAbove = spaceAbove >= spaceBelow;
        }

        var directionChanged = _inlineChatPointAbove != pointAbove;
        _inlineChatPointAbove = pointAbove;

        InlinePromptChatPanel.SetCalloutTail(
            pointAbove,
            showTail: true,
            tailOffset);
        if (directionChanged)
        {
            InlinePromptChatPanel.UpdateLayout();
            bubbleHeight = InlinePromptChatPanel.BubbleHeight;
            belowY = inputOrigin.Y + caretRect.Y + caretRect.Height + 2;
            aboveY = inputOrigin.Y + caretRect.Y - bubbleHeight - 2;
        }

        var y = pointAbove ? aboveY : belowY;
        SetPopupOffset(horizontal: false, y, rasterizationScale);
    }

    private void SetPopupOffset(bool horizontal, double value, double rasterizationScale)
    {
        var offset = SnapToPhysicalPixel(value, rasterizationScale);
        var currentOffset = horizontal
            ? InlinePromptPopup.HorizontalOffset
            : InlinePromptPopup.VerticalOffset;
        if (double.IsFinite(currentOffset) && Math.Abs(currentOffset - offset) < 0.001)
        {
            return;
        }

        if (horizontal)
        {
            InlinePromptPopup.HorizontalOffset = offset;
        }
        else
        {
            InlinePromptPopup.VerticalOffset = offset;
        }
    }

    private static double SnapToPhysicalPixel(double value, double rasterizationScale) =>
        Math.Round(value * rasterizationScale, MidpointRounding.AwayFromZero)
        / rasterizationScale;
}
