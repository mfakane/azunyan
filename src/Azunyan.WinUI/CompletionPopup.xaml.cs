using Azunyan.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;

namespace Azunyan.WinUI;

public sealed class CompletionAcceptedEventArgs : EventArgs
{
    public CompletionAcceptedEventArgs(
        CompletionResult result,
        CompletionItem item)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(item);
        Result = result;
        Item = item;
    }

    public CompletionResult Result { get; }

    public CompletionItem Item { get; }

    public TextRange ReplacementRange => Result.ReplacementRange;

    public bool Cancel { get; set; }
}

/// <summary>
/// Displays completion items independently from an editor document. The
/// consumer owns the provider and applies the accepted item's replacement
/// range to its input control.
/// </summary>
public sealed partial class CompletionPopup : UserControl
{
    private IReadOnlyList<CompletionItem>? _items;
    private CompletionResult? _result;
    private AzunyanColorScheme _colorScheme = AzunyanColorScheme.Default;
    private bool _updatingItems;

    public CompletionPopup()
    {
        InitializeComponent();
        CompletionList.ItemClick += CompletionList_ItemClick;
        CompletionList.SelectionChanged += CompletionList_SelectionChanged;
        CompletionList.AddHandler(
            UIElement.KeyDownEvent,
            new KeyEventHandler(CompletionList_KeyDown),
            true);
        ApplyColorScheme();
    }

    public event EventHandler<CompletionAcceptedEventArgs>? Accepted;

    public bool IsOpen => PopupHost.IsOpen;

    public bool HasFocus => CompletionList.FocusState != FocusState.Unfocused;

    public AzunyanColorScheme ColorScheme
    {
        get => _colorScheme;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _colorScheme = value;
            ApplyColorScheme();
        }
    }

    public void Show(
        CompletionResult result,
        double horizontalOffset,
        double verticalOffset,
        XamlRoot? xamlRoot = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (xamlRoot is null)
        {
            xamlRoot = XamlRoot;
        }

        if (xamlRoot is null)
        {
            return;
        }

        PopupHost.XamlRoot = xamlRoot;

        if (!ReferenceEquals(_result, result))
        {
            _updatingItems = true;
            try
            {
                CompletionList.Items.Clear();
                foreach (var item in result.Items)
                {
                    CompletionList.Items.Add(new TextBlock
                    {
                        Text = item.Label,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 14,
                        Foreground = new SolidColorBrush(_colorScheme.PopupForeground),
                        Padding = new Thickness(8, 4, 8, 4)
                    });
                }

                _items = result.Items;
                _result = result;
                CompletionList.SelectedIndex = result.Items.Count > 0 ? 0 : -1;
            }
            finally
            {
                _updatingItems = false;
            }
        }
        else if (CompletionList.SelectedIndex < 0 && result.Items.Count > 0)
        {
            CompletionList.SelectedIndex = 0;
        }

        UpdateDetails();
        PopupHost.HorizontalOffset = horizontalOffset;
        PopupHost.VerticalOffset = verticalOffset;
        PopupHost.IsOpen = result.Items.Count > 0;
    }

    public void Hide()
    {
        PopupHost.IsOpen = false;
        CompletionList.Items.Clear();
        CompletionList.SelectedIndex = -1;
        _items = null;
        _result = null;
        CompletionDetailsBorder.Visibility = Visibility.Collapsed;
        CompletionDetailsTitle.Text = string.Empty;
        CompletionDetailsContent.Text = string.Empty;
    }

    public void MoveSelection(int direction)
    {
        var count = CompletionList.Items.Count;
        if (count == 0)
        {
            return;
        }

        var index = CompletionList.SelectedIndex < 0 ? 0 : CompletionList.SelectedIndex;
        CompletionList.SelectedIndex = (index + direction + count) % count;
    }

    public bool TryAcceptSelected()
    {
        var selectedIndex = CompletionList.SelectedIndex;
        if (_result is null
            || _items is null
            || selectedIndex < 0
            || selectedIndex >= _items.Count)
        {
            return false;
        }

        var args = new CompletionAcceptedEventArgs(
            _result,
            _items[selectedIndex]);
        Accepted?.Invoke(this, args);
        if (args.Cancel)
        {
            return false;
        }

        Hide();
        return true;
    }

    public bool HandleKeyDown(VirtualKey key, bool shiftDown)
    {
        if (!IsOpen)
        {
            return false;
        }

        switch (key)
        {
            case VirtualKey.Down:
                MoveSelection(1);
                return true;
            case VirtualKey.Up:
                MoveSelection(-1);
                return true;
            case VirtualKey.Enter:
            case VirtualKey.Tab when !shiftDown:
                return TryAcceptSelected();
            case VirtualKey.Escape:
                Hide();
                return true;
            default:
                return false;
        }
    }

    private void CompletionList_ItemClick(object sender, ItemClickEventArgs args)
    {
        for (var index = 0; index < CompletionList.Items.Count; index++)
        {
            if (!ReferenceEquals(CompletionList.Items[index], args.ClickedItem))
            {
                continue;
            }

            CompletionList.SelectedIndex = index;
            TryAcceptSelected();
            return;
        }
    }

    private void CompletionList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (!_updatingItems && CompletionList.SelectedItem is { } selectedItem)
        {
            CompletionList.ScrollIntoView(selectedItem);
        }

        UpdateDetails();
    }

    private void CompletionList_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        var shiftDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (HandleKeyDown(args.Key, shiftDown))
        {
            args.Handled = true;
        }
    }

    private void UpdateDetails()
    {
        var selectedIndex = CompletionList.SelectedIndex;
        if (_items is null
            || selectedIndex < 0
            || selectedIndex >= _items.Count)
        {
            CompletionDetailsBorder.Visibility = Visibility.Collapsed;
            CompletionDetailsTitle.Text = string.Empty;
            CompletionDetailsContent.Text = string.Empty;
            return;
        }

        var item = _items[selectedIndex];
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

    private void ApplyColorScheme()
    {
        PopupBorder.Background = new SolidColorBrush(_colorScheme.PopupBackground);
        PopupBorder.BorderBrush = new SolidColorBrush(_colorScheme.PopupBorder);
        CompletionList.Foreground = new SolidColorBrush(_colorScheme.PopupForeground);
        CompletionDetailsBorder.Background = new SolidColorBrush(_colorScheme.PopupBackground);
        CompletionDetailsBorder.BorderBrush = new SolidColorBrush(_colorScheme.PopupBorder);
        CompletionDetailsTitle.Foreground = new SolidColorBrush(_colorScheme.PopupForeground);
        CompletionDetailsContent.Foreground = new SolidColorBrush(_colorScheme.PopupForeground);
    }
}
