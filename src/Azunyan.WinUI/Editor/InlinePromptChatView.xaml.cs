using System.Collections.ObjectModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace Azunyan.WinUI;

public sealed partial class InlinePromptChatView : UserControl
{
    private bool _isComposing;

    public ObservableCollection<InlinePromptChatEntry> Entries { get; } = new();

    public event EventHandler? SendRequested;

    public event EventHandler? DismissRequested;

    public event Action<string>? InsertRequested;

    public InlinePromptChatView()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => InlinePromptTitle.Text;
        set => InlinePromptTitle.Text = value;
    }

    public string InputText
    {
        get => InlinePromptInput.Text;
        set => InlinePromptInput.Text = value;
    }

    public double BubbleHeight => InlinePromptBubbleRoot.ActualHeight;

    public double BubbleWidth
    {
        get => InlinePromptBorder.Width;
        set => InlinePromptBorder.Width = value;
    }

    public void PreparePrompt(string title, string placeholder, bool clearInput)
    {
        if (clearInput)
        {
            InputText = string.Empty;
        }

        Title = title;
        InlinePromptInput.PlaceholderText = placeholder;
        InlinePromptInput.IsEnabled = true;
        InlinePromptCancelButton.Content = "Cancel";
        _isComposing = false;
        SetProcessing(isProcessing: false, canSend: true);
    }

    public void FocusInput() => InlinePromptInput.Focus(FocusState.Programmatic);

    public void FinishPrompt()
    {
        InlinePromptInput.IsEnabled = false;
        InlinePromptCancelButton.Content = "Close";
        _isComposing = false;
        SetProcessing(isProcessing: false, canSend: false);
    }

    public void SetDismissButtonContent(string content) =>
        InlinePromptCancelButton.Content = content;

    public void SetProcessing(bool isProcessing, bool canSend)
    {
        InlinePromptBusyIndicator.IsActive = isProcessing;
        InlinePromptBusyIndicator.Visibility = isProcessing
            ? Visibility.Visible
            : Visibility.Collapsed;
        InlinePromptSendIcon.Visibility = isProcessing
            ? Visibility.Collapsed
            : Visibility.Visible;
        InlinePromptSendButton.IsEnabled = !isProcessing && canSend;
        AutomationProperties.SetName(
            InlinePromptSendButton,
            isProcessing ? "Processing" : "Send message");
        ToolTipService.SetToolTip(
            InlinePromptSendButton,
            isProcessing ? "Processing" : "Send message");
    }

    public void AppendEntry(bool isUser, string text, bool canInsert)
    {
        Entries.Add(new InlinePromptChatEntry(isUser, text, canInsert));
        InlinePromptTranscriptScrollViewer.Visibility = Visibility.Visible;
        DispatcherQueue.TryEnqueue(() =>
        {
            InlinePromptTranscriptScrollViewer.UpdateLayout();
            InlinePromptTranscriptScrollViewer.ChangeView(
                null,
                InlinePromptTranscriptScrollViewer.ScrollableHeight,
                null,
                true);
        });
    }

    public void SetCalloutTail(bool pointAbove, bool showTail, double horizontalOffset)
    {
        InlinePromptTopTailFill.Visibility = showTail && !pointAbove
            ? Visibility.Visible
            : Visibility.Collapsed;
        InlinePromptTopTailOutline.Visibility = InlinePromptTopTailFill.Visibility;
        InlinePromptBottomTailFill.Visibility = showTail && pointAbove
            ? Visibility.Visible
            : Visibility.Collapsed;
        InlinePromptBottomTailOutline.Visibility = InlinePromptBottomTailFill.Visibility;

        var margin = new Thickness(horizontalOffset, pointAbove ? 0 : -1, 0, pointAbove ? -1 : 0);
        InlinePromptTopTailFill.Margin = margin;
        InlinePromptTopTailOutline.Margin = margin;
        InlinePromptBottomTailFill.Margin = margin;
        InlinePromptBottomTailOutline.Margin = margin;
    }

    private void InlinePromptInput_BeforeKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (_isComposing)
        {
            return;
        }

        if (args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            DismissRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (args.Key != VirtualKey.Enter
            || (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                & CoreVirtualKeyStates.Down) != 0)
        {
            return;
        }

        args.Handled = true;
        SendRequested?.Invoke(this, EventArgs.Empty);
    }

    private void InlinePromptInput_TextCompositionStarted(
        object sender,
        TextCompositionStartedEventArgs args) =>
        _isComposing = true;

    private void InlinePromptInput_TextCompositionEnded(
        object sender,
        TextCompositionEndedEventArgs args) =>
        _isComposing = false;

    private void InlinePromptSendButton_Click(object sender, RoutedEventArgs args) =>
        SendRequested?.Invoke(this, EventArgs.Empty);

    private void InlinePromptCancelButton_Click(object sender, RoutedEventArgs args) =>
        DismissRequested?.Invoke(this, EventArgs.Empty);

    private void InlinePromptCloseButton_Click(object sender, RoutedEventArgs args) =>
        DismissRequested?.Invoke(this, EventArgs.Empty);

    private void InlinePromptInsertButton_Click(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement element
            && element.DataContext is InlinePromptChatEntry entry
            && entry.CanInsert)
        {
            InsertRequested?.Invoke(entry.Text);
        }
    }

    private void InlinePromptCopyButton_Click(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement element
            && element.DataContext is InlinePromptChatEntry entry)
        {
            CopyEntry(entry.Text);
        }
    }

    private static void CopyEntry(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}

public sealed class InlinePromptChatEntry(bool isUser, string text, bool canInsert)
{
    public bool IsUser { get; } = isUser;

    public Visibility UserStyleVisibility => IsUser ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AssistantStyleVisibility => IsUser ? Visibility.Collapsed : Visibility.Visible;

    public string Text { get; } = text;

    public bool CanInsert { get; } = canInsert;
}
