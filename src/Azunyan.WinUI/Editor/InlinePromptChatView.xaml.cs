using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Azunyan.Core;
using Microsoft.UI.Dispatching;
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
    private static readonly TimeSpan SessionIdleTimeout = TimeSpan.FromMinutes(30);
    private readonly Dictionary<string, ChatSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, (string ConversationKey, ChatSession Session, InlinePromptChatEntry Entry)>
        _streamingEntries = [];
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _sessionExpiryTimer;
    private bool _isComposing;
    private string? _activeConversationKey;

    public ObservableCollection<InlinePromptChatEntry> Entries { get; } = new();

    public event EventHandler? SendRequested;

    public event EventHandler? DismissRequested;

    public event Action<string>? InsertRequested;

    public string? ActiveConversationKey => _activeConversationKey;

    public InlinePromptChatView()
    {
        InitializeComponent();
        _sessionExpiryTimer = DispatcherQueue.CreateTimer();
        _sessionExpiryTimer.Interval = TimeSpan.FromMinutes(1);
        _sessionExpiryTimer.Tick += SessionExpiryTimer_Tick;
        Loaded += InlinePromptChatView_Loaded;
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

    public void PreparePrompt(
        string conversationKey,
        string title,
        string placeholder,
        bool preserveChatHistory,
        bool clearInput)
    {
        ActivateConversation(conversationKey, preserveChatHistory);
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
        var isProcessing = _activeConversationKey is { } key
            && _sessions.TryGetValue(key, out var session)
            && session.IsBusy;
        SetProcessing(isProcessing, canSend: false);
    }

    public void SetDismissButtonContent(string content) =>
        InlinePromptCancelButton.Content = content;

    public void SetProcessing(bool isProcessing, bool canSend) =>
        SetProcessing(_activeConversationKey, isProcessing, canSend);

    public void SetProcessing(string? conversationKey, bool isProcessing, bool canSend)
    {
        if (conversationKey is not null
            && _sessions.TryGetValue(conversationKey, out var session))
        {
            session.IsBusy = isProcessing;
        }

        if (conversationKey is null || !IsActive(conversationKey))
        {
            return;
        }

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

    public void AppendEntry(
        string conversationKey,
        bool isUser,
        string text,
        bool canInsert)
    {
        var session = GetOrCreateSession(conversationKey);
        var entry = new InlinePromptChatEntry(isUser, text, canInsert);
        session.Entries.Add(entry);
        Touch(session);
        if (IsActive(conversationKey))
        {
            Entries.Add(entry);
            ShowTranscriptAndScroll();
        }
    }

    public ExternalToolPromptInput AppendUserEntryAndCreatePromptInput(
        string conversationKey,
        string text,
        bool canInsert)
    {
        var session = GetOrCreateSession(conversationKey);
        var entry = new InlinePromptChatEntry(true, text, canInsert);
        session.Entries.Add(entry);
        Touch(session);
        if (IsActive(conversationKey))
        {
            Entries.Add(entry);
            ShowTranscriptAndScroll();
        }

        using var historyStream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(historyStream))
        {
            writer.WriteStartArray();
            foreach (var message in session.Entries)
            {
                writer.WriteStartObject();
                writer.WriteString("role", message.IsUser ? "user" : "assistant");
                writer.WriteString("content", message.Text);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return new ExternalToolPromptInput(
            text,
            Encoding.UTF8.GetString(historyStream.ToArray()));
    }

    public void BeginStreamingEntry(
        string conversationKey,
        Guid responseId,
        bool canInsert)
    {
        var session = GetOrCreateSession(conversationKey);
        var entry = new InlinePromptChatEntry(false, string.Empty, canInsert);
        _streamingEntries.Add(responseId, (conversationKey, session, entry));
        session.OutstandingResponses++;
        session.Entries.Add(entry);
        Touch(session);
        if (IsActive(conversationKey))
        {
            Entries.Add(entry);
            ShowTranscriptAndScroll();
        }
    }

    public void AppendStreamingText(string conversationKey, Guid responseId, string text)
    {
        if (!_streamingEntries.TryGetValue(responseId, out var stream)
            || !string.Equals(
                stream.ConversationKey,
                conversationKey,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var entry = stream.Entry;
        entry.AppendText(text);
        if (IsActive(stream.ConversationKey))
        {
            ScrollTranscriptToBottom();
        }
    }

    public void FinishStreamingEntry(string conversationKey, Guid responseId)
    {
        if (_streamingEntries.TryGetValue(responseId, out var stream)
            && string.Equals(
                stream.ConversationKey,
                conversationKey,
                StringComparison.OrdinalIgnoreCase)
            && _streamingEntries.Remove(responseId))
        {
            stream.Session.OutstandingResponses--;
            Touch(stream.Session);
        }
    }

    public bool IsActiveConversation(string conversationKey) => IsActive(conversationKey);

    public void ActivateConversation(
        string conversationKey,
        bool? preserveChatHistory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationKey);
        ExpireSessions(DateTimeOffset.UtcNow);
        var session = GetOrCreateSession(conversationKey, preserveChatHistory);
        Touch(session);
        _activeConversationKey = conversationKey;
        Entries.Clear();
        foreach (var entry in session.Entries)
        {
            Entries.Add(entry);
        }

        UpdateTranscriptVisibility();
        SetProcessing(conversationKey, session.IsBusy, canSend: false);
        EnsureSessionExpiryTimer();
    }

    public void CloseConversation()
    {
        var discardedKeys = _sessions
            .Where(pair => !pair.Value.PreserveChatHistory)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (discardedKeys.Count == 0)
        {
            return;
        }

        foreach (var responseId in _streamingEntries
                     .Where(pair => discardedKeys.Contains(pair.Value.ConversationKey))
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _streamingEntries.Remove(responseId);
        }

        foreach (var key in discardedKeys)
        {
            _sessions.Remove(key);
        }

        if (_activeConversationKey is { } activeKey
            && _sessions.TryGetValue(activeKey, out var activeSession))
        {
            Touch(activeSession);
        }

        _activeConversationKey = null;
        Entries.Clear();
        UpdateTranscriptVisibility();
        EnsureSessionExpiryTimer();
    }

    private ChatSession GetOrCreateSession(
        string conversationKey,
        bool? preserveChatHistory = null)
    {
        if (!_sessions.TryGetValue(conversationKey, out var session))
        {
            session = new ChatSession();
            _sessions.Add(conversationKey, session);
        }

        if (preserveChatHistory is { } preserve)
        {
            session.PreserveChatHistory = preserve;
        }

        EnsureSessionExpiryTimer();
        return session;
    }

    private bool IsActive(string conversationKey) =>
        string.Equals(_activeConversationKey, conversationKey, StringComparison.OrdinalIgnoreCase);

    private static void Touch(ChatSession session) =>
        session.LastActivityUtc = DateTimeOffset.UtcNow;

    private void EnsureSessionExpiryTimer()
    {
        var hasPreservedSessions = _sessions.Values.Any(session => session.PreserveChatHistory);
        if (hasPreservedSessions && !_sessionExpiryTimer.IsRunning && IsLoaded)
        {
            _sessionExpiryTimer.Start();
        }
        else if (!hasPreservedSessions && _sessionExpiryTimer.IsRunning)
        {
            _sessionExpiryTimer.Stop();
        }
    }

    private void SessionExpiryTimer_Tick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        ExpireSessions(DateTimeOffset.UtcNow);
        if (!_sessions.Values.Any(session => session.PreserveChatHistory))
        {
            _sessionExpiryTimer.Stop();
        }
    }

    private void ExpireSessions(DateTimeOffset now)
    {
        var expiredKeys = _sessions
            .Where(pair => pair.Value.PreserveChatHistory
                && !IsActive(pair.Key)
                && !pair.Value.IsBusy
                && pair.Value.OutstandingResponses == 0
                && now - pair.Value.LastActivityUtc >= SessionIdleTimeout)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in expiredKeys)
        {
            _sessions.Remove(key);
            if (IsActive(key))
            {
                Entries.Clear();
                UpdateTranscriptVisibility();
            }
        }
    }

    private void InlinePromptChatView_Loaded(object sender, RoutedEventArgs args) =>
        EnsureSessionExpiryTimer();

    private void ShowTranscriptAndScroll()
    {
        InlinePromptTranscriptScrollViewer.Visibility = Visibility.Visible;
        ScrollTranscriptToBottom();
    }

    private void UpdateTranscriptVisibility() =>
        InlinePromptTranscriptScrollViewer.Visibility = Entries.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void ScrollTranscriptToBottom()
    {
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

    private sealed class ChatSession
    {
        public List<InlinePromptChatEntry> Entries { get; } = [];

        public bool PreserveChatHistory { get; set; }

        public DateTimeOffset LastActivityUtc { get; set; } = DateTimeOffset.UtcNow;

        public int OutstandingResponses { get; set; }

        public bool IsBusy { get; set; }
    }
}

public sealed class InlinePromptChatEntry(bool isUser, string text, bool canInsert)
    : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsUser { get; } = isUser;

    public Visibility UserStyleVisibility => IsUser ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AssistantStyleVisibility => IsUser ? Visibility.Collapsed : Visibility.Visible;

    public string Text { get; private set; } = text;

    public bool CanInsert { get; } = canInsert;

    public void AppendText(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        Text += text;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
    }
}
