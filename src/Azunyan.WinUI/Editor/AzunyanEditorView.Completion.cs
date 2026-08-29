using Azunyan.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Azunyan.WinUI;

public sealed partial class AzunyanEditorView
{
    private void UpdateCompletionPopup()
    {
        if (!IsLoaded
            || !IsProjectedTextSurface
            || InputEditor.IsComposing
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
            || completions.ReplacementRange.Start > InputEditor.Snapshot.Length
            || completions.ReplacementRange.End > InputEditor.Snapshot.Length
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

        InputEditor.AutoIndentOnEnter = false;
        InputEditor.SuppressVerticalCaretNavigation = true;

        if (!_defaultRenderer.TextRenderer.TryGetCaretRect(
                DocumentAnchor.Before(frame.Selection.CaretPosition),
                InputEditor.Padding.Left,
                InputEditor.Padding.Top,
                _scrollViewer?.HorizontalOffset ?? 0,
                GetVerticalOffset(),
                _characterWidth,
                _lineHeight,
                out var caretRect))
        {
            HideCompletionPopup();
            return;
        }

        if (!ReferenceEquals(_displayedCompletionItems, completions.Items))
        {
            CompletionList.Items.Clear();
            foreach (var item in completions.Items)
            {
                CompletionList.Items.Add(new TextBlock
                {
                    Text = item.Label,
                    Padding = new Thickness(8, 4, 8, 4)
                });
            }

            _displayedCompletionItems = completions.Items;
            CompletionList.SelectedIndex = 0;
        }
        else if (CompletionList.SelectedIndex < 0)
        {
            CompletionList.SelectedIndex = 0;
        }

        UpdateCompletionDetails();

        var inputOrigin = ProjectedSurfaceHost.TransformToVisual(RootGrid)
            .TransformPoint(new Point(0, 0));
        CompletionPopup.HorizontalOffset = inputOrigin.X + caretRect.X;
        CompletionPopup.VerticalOffset = inputOrigin.Y + caretRect.Y + caretRect.Height;
        CompletionPopup.IsOpen = true;
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
                InputEditor.Snapshot,
                position,
                InputEditor.Document.Selection,
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

        var frame = GetCurrentFrame();
        var tooltip = frame?.Position?.Tooltip;
        if (frame is null
            || tooltip is null
            || frame.Position!.Context.Position != _hoverPosition
            || tooltip.Range.Start > InputEditor.Snapshot.Length
            || tooltip.Range.End > InputEditor.Snapshot.Length)
        {
            HideTooltipPopup();
            return;
        }

        if (!_defaultRenderer.TextRenderer.TryGetCaretRect(
                DocumentAnchor.Before(_hoverPosition),
                InputEditor.Padding.Left,
                InputEditor.Padding.Top,
                _scrollViewer?.HorizontalOffset ?? 0,
                GetVerticalOffset(),
                _characterWidth,
                _lineHeight,
                out var anchorRect))
        {
            HideTooltipPopup();
            return;
        }

        TooltipTitle.Text = tooltip.Title ?? string.Empty;
        TooltipTitle.Visibility = string.IsNullOrEmpty(tooltip.Title)
            ? Visibility.Collapsed
            : Visibility.Visible;
        TooltipContent.Text = tooltip.Content;

        var inputOrigin = ProjectedSurfaceHost.TransformToVisual(RootGrid)
            .TransformPoint(new Point(0, 0));
        TooltipPopup.HorizontalOffset = inputOrigin.X + anchorRect.X;
        TooltipPopup.VerticalOffset = inputOrigin.Y + anchorRect.Y + anchorRect.Height + 4;
        TooltipPopup.IsOpen = true;
    }

    private void MoveCompletionSelection(int direction)
    {
        var count = CompletionList.Items.Count;
        if (count == 0)
        {
            return;
        }

        var index = CompletionList.SelectedIndex < 0 ? 0 : CompletionList.SelectedIndex;
        CompletionList.SelectedIndex = (index + direction + count) % count;
    }

    private void OnCompletionSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (CompletionList.SelectedItem is { } selectedItem)
        {
            CompletionList.ScrollIntoView(selectedItem);
        }

        UpdateCompletionDetails();
    }

    private void UpdateCompletionDetails()
    {
        var selectedIndex = CompletionList.SelectedIndex;
        if (_displayedCompletionItems is not { } items
            || selectedIndex < 0
            || selectedIndex >= items.Count)
        {
            CompletionDetailsBorder.Visibility = Visibility.Collapsed;
            CompletionDetailsTitle.Text = string.Empty;
            CompletionDetailsContent.Text = string.Empty;
            return;
        }

        var item = items[selectedIndex];
        CompletionDetailsTitle.Text = item.Detail ?? string.Empty;
        CompletionDetailsTitle.Visibility = string.IsNullOrEmpty(item.Detail)
            ? Visibility.Collapsed
            : Visibility.Visible;
        CompletionDetailsContent.Text = item.Documentation ?? string.Empty;
        CompletionDetailsBorder.Visibility = string.IsNullOrEmpty(item.Detail)
            && string.IsNullOrEmpty(item.Documentation)
                ? Visibility.Collapsed
                : Visibility.Visible;
    }

    private void OnCompletionItemClick(object sender, ItemClickEventArgs args)
    {
        for (var index = 0; index < CompletionList.Items.Count; index++)
        {
            if (ReferenceEquals(CompletionList.Items[index], args.ClickedItem))
            {
                CompletionList.SelectedIndex = index;
                TryAcceptSelectedCompletion();
                return;
            }
        }
    }

    private bool TryAcceptSelectedCompletion()
    {
        var selectedIndex = CompletionList.SelectedIndex;
        if (GetCurrentFrame()?.Position?.Completions is not { } completions
            || !ReferenceEquals(_displayedCompletionItems, completions.Items)
            || selectedIndex < 0
            || selectedIndex >= completions.Items.Count)
        {
            HideCompletionPopup();
            _completionRequested = false;
            _explicitCompletionRequested = false;
            return false;
        }

        var item = completions.Items[selectedIndex];
        var frame = GetCurrentFrame();
        var range = completions.ReplacementRange;
        if (frame is null
            || frame.Position!.Context.Position != frame.Selection.CaretPosition
            || frame.Selection.CaretPosition < range.Start
            || frame.Selection.CaretPosition > range.End
            || range.Start > InputEditor.Snapshot.Length
            || range.End > InputEditor.Snapshot.Length
            || (!_explicitCompletionRequested && !HasUsefulCompletion(completions)))
        {
            HideCompletionPopup();
            _completionRequested = false;
            _explicitCompletionRequested = false;
            return false;
        }

        _completionRequested = false;
        _explicitCompletionRequested = false;
        HideCompletionPopup();
        _applyingCompletion = true;
        try
        {
            InputEditor.ReplaceDocumentRange(range, item.InsertText);
        }
        finally
        {
            _applyingCompletion = false;
        }

        InputEditor.Focus(FocusState.Programmatic);
        return true;
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
        var position = InputEditor.Document.Selection.CaretPosition;
        var text = InputEditor.Snapshot.Text;
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
        InputEditor.AutoIndentOnEnter = true;
        InputEditor.SuppressVerticalCaretNavigation = false;
        CompletionPopup.IsOpen = false;
        CompletionList.Items.Clear();
        _displayedCompletionItems = null;
        CompletionList.SelectedIndex = -1;
        CompletionDetailsTitle.Visibility = Visibility.Visible;
        CompletionDetailsBorder.Visibility = Visibility.Collapsed;
        CompletionDetailsTitle.Text = string.Empty;
        CompletionDetailsContent.Text = string.Empty;
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
