using Azunyan.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Azunyan.WinUI;

public sealed partial class AzunyanEditorView
{
    private void UpdateCompletionPopup()
    {
        if (IsReadOnly
            || !IsLoaded
            || !IsProjectedTextSurface
            || IsComposing
            || !_completionRequested)
        {
            HideCompletionPopup();
            return;
        }

        var frame = GetCurrentFrame();
        if (frame is { Position: null }
            && CompletionPopup.IsOpen
            && TryGetShownCompletionRange(out var shownRange))
        {
            // The candidates for the edit are on their way; the ones shown
            // stay until they arrive, narrowed to the word as typed, instead
            // of the popup blinking out.
            var narrowed = NarrowShownCompletion(shownRange);
            if (narrowed.Items.Count > 0
                && (_explicitCompletionRequested || HasUsefulCompletion(narrowed)))
            {
                ShowCompletionPopup(narrowed);
            }
            else
            {
                HideCompletionPopup();
            }

            return;
        }

        var completions = frame?.Position?.Completions;
        StopCompletionSessionIfEmpty(frame, completions);
        if (frame is null
            || completions is null
            || completions.Items.Count == 0
            || completions.ReplacementRange.Start > Snapshot.Length
            || completions.ReplacementRange.End > Snapshot.Length
            || frame.Position!.Context.Position != frame.Selection.CaretPosition
            || frame.Selection.CaretPosition < completions.ReplacementRange.Start
            || frame.Selection.CaretPosition > completions.ReplacementRange.End)
        {
            HideCompletionPopup();
            return;
        }

        if (!_explicitCompletionRequested && !HasUsefulCompletion(completions))
        {
            HideCompletionPopup();
            return;
        }

        _shownCompletion = frame.Position;
        _narrowedCompletion = null;
        ShowCompletionPopup(completions);
    }

    private void ShowCompletionPopup(CompletionResult completions)
    {
        _autoIndentOnEnter = false;
        _suppressVerticalCaretNavigation = true;

        if (!TryGetRendererCaretRect(
                DocumentAnchor.Before(Document.Selection.CaretPosition),
                out var caretRect))
        {
            HideCompletionPopup();
            return;
        }

        var inputOrigin = ProjectedSurfaceHost.TransformToVisual(EditorHost)
            .TransformPoint(new Point(0, 0));
        CompletionPopup.Show(
            completions,
            inputOrigin.X + caretRect.X,
            inputOrigin.Y + caretRect.Y + caretRect.Height);
    }

    /// <summary>
    /// The shown candidates that still match the word at the caret. They are
    /// narrowed again only when the word changes, so the selection stays
    /// while nothing is typed.
    /// </summary>
    private CompletionResult NarrowShownCompletion(TextRange range)
    {
        var completions = _shownCompletion!.Completions!;
        var word = Snapshot.GetText(range.Start, Document.Selection.CaretPosition - range.Start);
        if (range == completions.ReplacementRange
            && ReferenceEquals(Snapshot, _shownCompletion.Context.Snapshot))
        {
            return completions;
        }

        if (_narrowedCompletion is not { } narrowed
            || narrowed.Word != word
            || narrowed.Result.ReplacementRange != range)
        {
            narrowed = (word, completions.Narrow(range, word));
            _narrowedCompletion = narrowed;
        }

        return narrowed.Result;
    }

    /// <summary>
    /// Finds what the shown candidates replace in the current text, which
    /// they still can while an edit since they were asked for has only typed
    /// or erased the word at the caret.
    /// </summary>
    private bool TryGetShownCompletionRange(out TextRange range)
    {
        range = default;
        if (_shownCompletion is not { Completions: { } completions } shown)
        {
            return false;
        }

        var snapshot = Snapshot;
        var oldSnapshot = shown.Context.Snapshot;
        var oldCaret = shown.Context.Position;
        var start = completions.ReplacementRange.Start;
        var caret = Document.Selection.CaretPosition;
        var kept = Math.Min(caret, oldCaret);
        if (caret < start || oldCaret < start || kept > snapshot.Length)
        {
            return false;
        }

        if (!string.Equals(
                oldSnapshot.GetText(start, kept - start),
                snapshot.GetText(start, kept - start),
                StringComparison.Ordinal)
            || !snapshot.GetText(kept, caret - kept).All(IsIdentifierPart))
        {
            return false;
        }

        var end = caret + Math.Max(0, completions.ReplacementRange.End - oldCaret);
        if (end > snapshot.Length)
        {
            return false;
        }

        range = TextRange.FromBounds(start, end);
        return true;
    }

    /// <summary>
    /// Asks what the pointer rests on once it has stayed at one position for
    /// a moment, which is when a tooltip is wanted; passing over text asks
    /// nothing. An answer for an earlier position is dropped.
    /// </summary>
    private void ScheduleHoverRequest()
    {
        NextProviderGeneration(ref _hoverProviderGeneration);
        _hoverResult = null;
        _hoverTimer ??= DispatcherQueue.CreateTimer();
        _hoverTimer.Interval = HoverDelay;
        _hoverTimer.IsRepeating = false;
        _hoverTimer.Stop();
        _hoverTimer.Tick -= OnHoverTimerTick;
        _hoverTimer.Tick += OnHoverTimerTick;
        _hoverTimer.Start();
    }

    private void OnHoverTimerTick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        sender.Stop();
        if (_hoverPosition >= 0 && _hoverLinkRange is null)
        {
            RequestHoverProvider(_hoverPosition);
        }
    }

    private void RequestHoverProvider(int position)
    {
        if (!IsLoaded)
        {
            return;
        }

        var generation = NextProviderGeneration(ref _hoverProviderGeneration);
        _hoverResult = null;
        _ = ApplyHoverProviderResultAsync(
            _providerScheduler.RequestHoverAsync(
                Snapshot,
                position,
                Document.Selection),
            generation);
    }

    /// <summary>
    /// Keeps what the pointer rests on apart from the provider frame, whose
    /// position results belong to the caret and its completions.
    /// </summary>
    private async Task ApplyHoverProviderResultAsync(
        Task<PositionProviderResults?> request,
        long generation)
    {
        PositionProviderResults? result;
        try
        {
            result = await request.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return;
        }

        if (result is null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsLoaded
                || generation != _hoverProviderGeneration
                || !ReferenceEquals(result.Context.Snapshot, Snapshot))
            {
                return;
            }

            _hoverResult = result;
            UpdateTooltipPopup();
        });
    }

    private void UpdateTooltipPopup()
    {
        if (!IsLoaded || !IsProjectedTextSurface || _hoverPosition < 0)
        {
            HideTooltipPopup();
            return;
        }

        // The editor answers a link itself: the host learns how to open one
        // without having to supply a tooltip provider for it.
        if (_hoverLinkRange is { } linkRange
            && linkRange.End <= Snapshot.Length
            && !string.IsNullOrEmpty(LinkNavigationHint))
        {
            ShowTooltipPopup(_hoverLinkText, LinkNavigationHint, linkRange.Start);
            return;
        }

        var hover = _hoverResult;
        var tooltip = hover?.Tooltip;
        if (hover is null
            || tooltip is null
            || hover.Context.Position != _hoverPosition
            || !ReferenceEquals(hover.Context.Snapshot, Snapshot)
            || tooltip.Range.Start > Snapshot.Length
            || tooltip.Range.End > Snapshot.Length)
        {
            HideTooltipPopup();
            return;
        }

        ShowTooltipPopup(tooltip.Title, tooltip.Content, _hoverPosition);
    }

    /// <summary>
    /// Places the tooltip under the anchor position. A position scrolled out
    /// of the realized rows has no geometry, so the tooltip is hidden instead
    /// of left behind at a stale place.
    /// </summary>
    private void ShowTooltipPopup(string? title, string content, int anchorPosition)
    {
        if (!TryGetRendererCaretRect(
                DocumentAnchor.Before(anchorPosition),
                out var anchorRect)
            && !TryGetRendererCaretRect(
                DocumentAnchor.Before(_hoverPosition),
                out anchorRect))
        {
            HideTooltipPopup();
            return;
        }

        TooltipTitle.Text = title ?? string.Empty;
        TooltipTitle.Visibility = string.IsNullOrEmpty(title)
            ? Visibility.Collapsed
            : Visibility.Visible;
        TooltipContent.Text = content;

        var inputOrigin = ProjectedSurfaceHost.TransformToVisual(RootGrid)
            .TransformPoint(new Point(0, 0));
        // A link can begin left of the viewport, so keep the tooltip against
        // the surface edge instead of letting it slide off the window.
        TooltipPopup.HorizontalOffset = Math.Max(
            inputOrigin.X,
            inputOrigin.X + anchorRect.X);
        // The completion list opens under the caret's line, so the tooltip
        // goes above its own line while the list is open, and above the
        // signature help that is there too.
        if (IsCompletionPopupOpen)
        {
            TooltipBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var top = inputOrigin.Y + anchorRect.Y;
            if (IsSignatureHelpPopupOpen)
            {
                top = Math.Min(top, SignatureHelpPopup.VerticalOffset);
            }

            TooltipPopup.VerticalOffset = top - TooltipBorder.DesiredSize.Height - 4;
        }
        else
        {
            TooltipPopup.VerticalOffset = inputOrigin.Y + anchorRect.Y + anchorRect.Height + 4;
        }
        TooltipPopup.IsOpen = true;
    }

    private bool TryAcceptSelectedCompletion()
    {
        return CompletionPopup.TryAcceptSelected();
    }

    private void CompletionPopup_Accepted(
        object? sender,
        CompletionAcceptedEventArgs args)
    {
        var frame = GetCurrentFrame();
        var completions = frame?.Position?.Completions;
        var range = args.ReplacementRange;
        var isPending = frame is { Position: null }
            && (ReferenceEquals(args.Result, _shownCompletion?.Completions)
                || ReferenceEquals(args.Result, _narrowedCompletion?.Result))
            && TryGetShownCompletionRange(out range);
        if (!isPending
            && (frame is null
                || frame.Position is null
                || completions is null
                || !ReferenceEquals(args.Result, completions)
                || frame.Position!.Context.Position != frame.Selection.CaretPosition
                || frame.Selection.CaretPosition < range.Start
                || frame.Selection.CaretPosition > range.End
                || range.Start > Snapshot.Length
                || range.End > Snapshot.Length
                || (!_explicitCompletionRequested && !HasUsefulCompletion(completions))))
        {
            args.Cancel = true;
            _completionRequested = false;
            _explicitCompletionRequested = false;
            HideCompletionPopup();
            return;
        }

        _completionRequested = false;
        _explicitCompletionRequested = false;
        HideCompletionPopup();
        _applyingCompletion = true;
        try
        {
            ReplaceDocumentRange(range, args.Item.InsertText);
        }
        finally
        {
            _applyingCompletion = false;
        }

        Focus(FocusState.Programmatic);
    }

    private bool HasCompletionPrefix() => GetCompletionPrefix().Length > 0;

    private bool IsCompletionTrigger(TextSnapshot snapshot, int caretPosition) =>
        EndsWithTrigger(_completionTriggerCharacters, snapshot, caretPosition);

    private static bool EndsWithTrigger(string[] triggers, TextSnapshot snapshot, int caretPosition)
    {
        if (triggers.Length == 0
            || caretPosition <= 0
            || caretPosition > snapshot.Length)
        {
            return false;
        }

        return triggers.Any(trigger =>
            trigger.Length <= caretPosition
            && string.Equals(
                snapshot.GetText(caretPosition - trigger.Length, trigger.Length),
                trigger,
                StringComparison.Ordinal));
    }

    private string GetCompletionPrefix()
    {
        var position = Document.Selection.CaretPosition;
        var text = Snapshot.Text;
        var start = position;
        while (start > 0 && IsIdentifierPart(text[start - 1]))
        {
            start--;
        }

        return text[start..position];
    }

    private static bool IsIdentifierPart(char value) => char.IsLetterOrDigit(value) || value == '_';

    private bool HasUsefulCompletion(CompletionResult completions)
    {
        var prefix = GetCompletionPrefix();
        return completions.Items.Any(item =>
            !string.Equals(item.InsertText, prefix, StringComparison.OrdinalIgnoreCase));
    }

    private void HideCompletionPopup()
    {
        _autoIndentOnEnter = true;
        _suppressVerticalCaretNavigation = false;
        _shownCompletion = null;
        _narrowedCompletion = null;
        CompletionPopup.Hide();
    }

    private void StopCompletionSessionIfEmpty(
        EditorProviderFrame? frame,
        CompletionResult? completions)
    {
        if (!_completionRequested
            || frame?.Position is not { } position
            || position.Context.Position != frame.Selection.CaretPosition)
        {
            return;
        }

        if (completions is null
            || completions.Items.Count == 0
            || !HasUsefulCompletion(completions))
        {
            _completionRequested = false;
            _explicitCompletionRequested = false;
        }
    }

    private void HideTooltipPopup()
    {
        TooltipPopup.IsOpen = false;
        TooltipTitle.Text = string.Empty;
        TooltipContent.Text = string.Empty;
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
