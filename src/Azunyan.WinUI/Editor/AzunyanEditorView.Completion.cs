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

        _autoIndentOnEnter = false;
        _suppressVerticalCaretNavigation = true;

        if (!TryGetRendererCaretRect(
                DocumentAnchor.Before(frame.Selection.CaretPosition),
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

    private void RequestHoverProvider(int position)
    {
        if (!IsLoaded)
        {
            return;
        }

        var generation = NextProviderGeneration(ref _positionProviderGeneration);
        _ = ApplyPositionProviderResultAsync(
            _providerScheduler.RequestPositionAsync(
            Snapshot,
                position,
            Document.Selection,
                includeCompletion: false),
            generation);
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

        var frame = GetCurrentFrame();
        var tooltip = frame?.Position?.Tooltip;
        if (frame is null
            || tooltip is null
            || frame.Position!.Context.Position != _hoverPosition
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
        TooltipPopup.VerticalOffset = inputOrigin.Y + anchorRect.Y + anchorRect.Height + 4;
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
        if (frame is null
            || frame.Position is null
            || completions is null
            || !ReferenceEquals(args.Result, completions)
            || frame.Position!.Context.Position != frame.Selection.CaretPosition
            || frame.Selection.CaretPosition < range.Start
            || frame.Selection.CaretPosition > range.End
            || range.Start > Snapshot.Length
            || range.End > Snapshot.Length
            || (!_explicitCompletionRequested && !HasUsefulCompletion(completions)))
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

    private bool IsCompletionTrigger(string text, int caretPosition)
    {
        if (_completionTriggerCharacters.Length == 0
            || caretPosition <= 0
            || caretPosition > text.Length)
        {
            return false;
        }

        return _completionTriggerCharacters.Any(trigger =>
            trigger.Length <= caretPosition
            && text.AsSpan(caretPosition - trigger.Length, trigger.Length)
                .SequenceEqual(trigger.AsSpan()));
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
