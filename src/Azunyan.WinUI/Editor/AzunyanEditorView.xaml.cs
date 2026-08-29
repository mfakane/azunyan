using Azunyan.Core;
using System.Globalization;
using System.Runtime.ExceptionServices;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using Windows.UI;

namespace Azunyan.WinUI;

/// <summary>
/// Reusable editor host. The inner TextBox remains the native text-service and
/// IME host, while the gutter and overlay are independent rendering layers.
/// The native TextBox remains the input and IME host. The default renderer
/// draws the visible text from snapshot-bound projection data, so native glyphs
/// are not painted underneath a second syntax layer.
/// </summary>
public sealed partial class AzunyanEditorView : UserControl, IDisposable
{
    private readonly AzunyanEditorRenderer _defaultRenderer;
    private Document _document = new();
    private readonly SlidingInputWindowCalculator _inputWindowCalculator = new();
    private TextRange? _compositionRange;
    private long _inputWindowGeneration;
    private bool _synchronizingInputWindow;
    private bool _autoIndentOnEnter = true;
    private bool _suppressVerticalCaretNavigation;
    private int? _indentSize;
    private IndentationInputMode _indentationInputMode;
    private AzunyanColorScheme _colorScheme;
    private readonly EditorProviderSet _providers = new();
    private readonly EditorProviderScheduler _providerScheduler;
    private ScrollViewer? _scrollViewer;
    private double _lineHeight = 18;
    private double _characterWidth = 8;
    private IAzunyanEditorRenderer? _renderer;
    private EditorProviderFrame? _providerFrame;
    private readonly HashSet<string> _collapsedFoldIds = new(StringComparer.Ordinal);
    private double _projectedVerticalOffset;
    private bool _synchronizingProjectedScroll;
    private bool _preservingViewport;
    private bool _completionRequested;
    private bool _explicitCompletionRequested;
    private bool _applyingCompletion;
    private IReadOnlyList<CompletionItem>? _displayedCompletionItems;
    private string[] _completionTriggerCharacters = Array.Empty<string>();
    private bool _disposed;
    private int _hoverPosition = -1;
    private string _projectedAutomationStructureKey = string.Empty;
    private long _documentProviderGeneration;
    private long _viewportProviderGeneration;
    private long _positionProviderGeneration;
    private DocumentChangedEventArgs? _pendingProviderDocumentChange;
    private DocumentChangedEventArgs? _pendingAutomationDocumentChange;
    private AzunyanEditorViewAutomationPeer? _automationPeer;

    public AzunyanEditorView()
    {
        InitializeComponent();
        _colorScheme = AzunyanColorScheme.Default;
        _defaultRenderer = new AzunyanEditorRenderer(
            GutterDrawingSurface,
            TextDrawingSurface,
            GutterCanvas,
            TextRenderLayer,
            RenderOverlay);
        _defaultRenderer.LayoutInvalidated += OnRendererLayoutInvalidated;
        _providerScheduler = new EditorProviderScheduler(_providers);
        _renderer = _defaultRenderer;

        _document.Changed += OnInputDocumentChanged;
        _document.SelectionChanged += OnDocumentSelectionChanged;
        InputWindow.NativeTextBoxControl.Padding = new Thickness(8, 6, 8, 6);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ProjectedSurfaceHost.SizeChanged += OnSizeChanged;
        InputWindow.InputChanged += OnInputTextChanged;
        InputWindow.InputSelectionChanged += OnInputSelectionChanged;
        InputWindow.CompositionChanged += OnInputCompositionChanged;
        InputWindow.NativeFocusChanged += OnInputFocusChanged;
        InputWindow.AddHandler(
            UIElement.KeyDownEvent,
            new KeyEventHandler(OnInputKeyDown),
            true);
        EditorPointerSurface.PointerPressed += OnInputPointerPressed;
        EditorPointerSurface.PointerMoved += OnInputPointerMoved;
        EditorPointerSurface.PointerExited += OnInputPointerExited;
        CompletionList.ItemClick += OnCompletionItemClick;
        CompletionList.SelectionChanged += OnCompletionSelectionChanged;
        CompletionList.AddHandler(
            UIElement.KeyDownEvent,
            new KeyEventHandler(OnCompletionListKeyDown),
            true);
        ProjectedVerticalScrollBar.ValueChanged += OnProjectedVerticalScrollChanged;
        InputWindow.NativeTextBoxControl.AllowDrop = true;
        InputWindow.NativeTextBoxControl.IsSpellCheckEnabled = false;
        InputWindow.NativeTextBoxControl.IsTextPredictionEnabled = false;
        ApplyColorScheme();
        UpdateTextSurfaceMode();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _providerScheduler.Dispose();
        if (_renderer is IDisposable renderer
            && !ReferenceEquals(_renderer, _defaultRenderer))
        {
            renderer.Dispose();
        }

        _defaultRenderer.Dispose();
    }

    public static readonly DependencyProperty ShowLineNumbersProperty =
        DependencyProperty.Register(
            nameof(ShowLineNumbers),
            typeof(bool),
            typeof(AzunyanEditorView),
            new PropertyMetadata(true, OnRenderPropertyChanged));

    public static readonly DependencyProperty TextWrappingProperty =
        DependencyProperty.Register(
            nameof(TextWrapping),
            typeof(TextWrapping),
            typeof(AzunyanEditorView),
            new PropertyMetadata(TextWrapping.NoWrap, OnTextWrappingChanged));

    public static readonly DependencyProperty TabDisplaySizeProperty =
        DependencyProperty.Register(
            nameof(TabDisplaySize),
            typeof(int),
            typeof(AzunyanEditorView),
            new PropertyMetadata(4, OnTabDisplaySizeChanged));

    public static readonly DependencyProperty IndentSizeProperty =
        DependencyProperty.Register(
            nameof(IndentSize),
            typeof(int?),
            typeof(AzunyanEditorView),
            new PropertyMetadata(null, OnIndentSizeChanged));

    public static readonly DependencyProperty IndentationInputModeProperty =
        DependencyProperty.Register(
            nameof(IndentationInputMode),
            typeof(IndentationInputMode),
            typeof(AzunyanEditorView),
            new PropertyMetadata(IndentationInputMode.Auto, OnIndentationInputModeChanged));

    public static readonly DependencyProperty AcceptsReturnProperty =
        DependencyProperty.Register(
            nameof(AcceptsReturn),
            typeof(bool),
            typeof(AzunyanEditorView),
            new PropertyMetadata(true, OnAcceptsReturnChanged));

    public event TextChangedEventHandler? TextChanged
    {
        add => InputWindow.NativeTextBoxControl.TextChanged += value;
        remove => InputWindow.NativeTextBoxControl.TextChanged -= value;
    }

    public event RoutedEventHandler? SelectionChanged
    {
        add => InputWindow.NativeTextBoxControl.SelectionChanged += value;
        remove => InputWindow.NativeTextBoxControl.SelectionChanged -= value;
    }

    public event EventHandler<DocumentChangedEventArgs>? DocumentChanged
    {
        add => _document.Changed += value;
        remove => _document.Changed -= value;
    }

    /// <summary>
    /// Raised when the native text service starts, updates, or ends an IME
    /// composition. The projected renderer consumes the same state and draws
    /// its transient composition decoration from the current snapshot.
    /// </summary>
    public event EventHandler? CompositionChanged;

    public Document Document => _document;

    public TextSnapshot Snapshot => _document.Snapshot;

    public bool IsComposing => _compositionRange is not null;

    public TextRange? CompositionRange => _compositionRange;

    /// <summary>
    /// Providers are called with immutable snapshots and are safe to replace
    /// while the editor is running. Call <see cref="RefreshProviders"/> after
    /// mutating this set.
    /// </summary>
    public EditorProviderSet Providers => _providers;

    /// <summary>
    /// Literal strings which request completion after they are inserted. The
    /// host supplies this value from the active language mode.
    /// </summary>
    public IReadOnlyList<string> CompletionTriggerCharacters
    {
        get => _completionTriggerCharacters;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _completionTriggerCharacters = value
                .Where(trigger => !string.IsNullOrEmpty(trigger))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            _completionRequested = false;
            _explicitCompletionRequested = false;
            if (IsLoaded)
            {
                HideCompletionPopup();
            }
        }
    }

    public EditorProviderFrame? ProviderFrame => _providerFrame;

    /// <summary>
    /// Palette consumed by the default Azunyan renderer and transient editor
    /// UI. The host application may replace the default scheme with its own
    /// system or editor theme.
    /// </summary>
    public AzunyanColorScheme ColorScheme
    {
        get => _colorScheme;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _colorScheme = value;
            ApplyColorScheme();
            RenderViewport();
        }
    }

    /// <summary>
    /// Compatibility view of the channel frame for existing renderers.
    /// </summary>
    public EditorProviderResults? ProviderResults => _providerFrame?.ToLegacyResults();

    public event EventHandler<EditorProviderResultsEventArgs>? ProviderResultsChanged;

    public event EventHandler<EditorProviderFrameEventArgs>? ProviderFrameChanged;

    /// <summary>
    /// Raised when the projected surface is clicked on an interactive inlay
    /// or block adornment. The host owns command dispatch.
    /// </summary>
    public event EventHandler<AdornmentInvokedEventArgs>? AdornmentInvoked;

    public IReadOnlySet<string> CollapsedFoldIds => _collapsedFoldIds;

    internal AzunyanTextInputWindow InputHost => InputWindow;

    public string Text
    {
        get => Snapshot.Text;
        set => SetText(value);
    }

    public int SelectionStart
    {
        get => Document.Selection.Start;
        set => SetDocumentSelection(new TextSelection(value, value + SelectionLength));
    }

    public int SelectionLength
    {
        get => Document.Selection.Length;
        set => SetDocumentSelection(new TextSelection(SelectionStart, SelectionStart + value));
    }

    public string SelectedText
    {
        get => Snapshot.GetText(Document.Selection.Range);
        set => ReplaceDocumentRange(Document.Selection.Range, value);
    }

    public bool ShowLineNumbers
    {
        get => (bool)GetValue(ShowLineNumbersProperty);
        set => SetValue(ShowLineNumbersProperty, value);
    }

    public TextWrapping TextWrapping
    {
        get => (TextWrapping)GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    public int TabDisplaySize
    {
        get => (int)GetValue(TabDisplaySizeProperty);
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            SetValue(TabDisplaySizeProperty, value);
        }
    }

    public int? IndentSize
    {
        get => (int?)GetValue(IndentSizeProperty);
        set
        {
            if (value is { } size)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
            }

            SetValue(IndentSizeProperty, value);
        }
    }

    public IndentationInputMode IndentationInputMode
    {
        get => (IndentationInputMode)GetValue(IndentationInputModeProperty);
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            SetValue(IndentationInputModeProperty, value);
        }
    }

    public bool AcceptsReturn
    {
        get => (bool)GetValue(AcceptsReturnProperty);
        set => SetValue(AcceptsReturnProperty, value);
    }

    public IAzunyanEditorRenderer? Renderer
    {
        get => _renderer;
        set
        {
            _renderer = value;
            UpdateTextSurfaceMode();
            RenderViewport();
        }
    }

    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var oldText = Snapshot.Text;
        _document.Changed -= OnInputDocumentChanged;
        _document.SelectionChanged -= OnDocumentSelectionChanged;
        _compositionRange = null;
        _document = new Document(text);
        _document.Changed += OnInputDocumentChanged;
        _document.SelectionChanged += OnDocumentSelectionChanged;
        SyncInputWindow();
        RenderViewport();
        _automationPeer?.NotifyTextChanged(oldText, Snapshot.Text);
    }

    public void SetDocumentSelection(TextSelection selection)
    {
        _document.Selection = selection;
        SyncInputWindow();
    }

    public void ReplaceDocumentRange(TextRange range, string replacement) =>
        ReplaceDocumentRangeAndNotify(range, replacement);

    internal void SetAutomationValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.Equals(value, Snapshot.Text, StringComparison.Ordinal))
        {
            return;
        }

        ReplaceDocumentRange(
            TextRange.FromBounds(0, Snapshot.Length),
            value);
    }

    internal void InvokeOnEditorThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        InvokeOnEditorThread<object?>(() =>
        {
            action();
            return null;
        });
    }

    internal T InvokeOnEditorThread<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (DispatcherQueue.HasThreadAccess)
        {
            return action();
        }

        T result = default!;
        Exception? exception = null;
        using var completed = new ManualResetEventSlim();
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    result = action();
                }
                catch (Exception error)
                {
                    exception = error;
                }
                finally
                {
                    completed.Set();
                }
            }))
        {
            throw new InvalidOperationException(
                "The editor dispatcher is no longer available.");
        }

        if (!completed.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException(
                "The editor dispatcher did not process the automation request.");
        }

        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        return result;
    }

    public bool UndoDocument()
    {
        var result = _document.Undo();
        if (result)
        {
            SyncInputWindow();
        }

        return result;
    }

    public bool RedoDocument()
    {
        var result = _document.Redo();
        if (result)
        {
            SyncInputWindow();
        }

        return result;
    }

    public void CutSelectionToClipboard()
    {
        SyncInputWindow();
        InputWindow.NativeTextBoxControl.CutSelectionToClipboard();
        if (Document.Selection.Length > 0)
        {
            Document.DeleteSelection();
            SyncInputWindow();
        }
    }

    public void CopySelectionToClipboard()
    {
        SyncInputWindow();
        InputWindow.NativeTextBoxControl.CopySelectionToClipboard();
    }

    public void PasteFromClipboard()
    {
        SyncInputWindow();
        InputWindow.NativeTextBoxControl.PasteFromClipboard();
    }

    public void SelectAll()
    {
        _document.Select(TextRange.FromBounds(0, Snapshot.Length));
        SyncInputWindow();
    }

    public void Select(int start, int length)
    {
        _document.Select(new TextRange(start, length));
        SyncInputWindow();
    }

    public void RefreshProviders() => RequestProviderResults(true, true, true);

    /// <summary>Requests completion explicitly, independent of the current prefix.</summary>
    public void RequestCompletion()
    {
        if (!IsLoaded || IsComposing)
        {
            return;
        }

        _explicitCompletionRequested = true;
        _completionRequested = true;
        RequestProviderResults(false, false, true, requestCompletion: true);
    }

    public void SetFoldCollapsed(string foldId, bool collapsed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(foldId);
        if (collapsed)
        {
            var fold = GetCurrentFrame()?.Document?.Folds
                .FirstOrDefault(candidate => candidate.Id == foldId);
            if (fold is not null
                && fold.Range.Contains(Document.Selection.CaretPosition))
            {
                SetDocumentSelection(TextSelection.Caret(fold.Range.Start));
            }

            _collapsedFoldIds.Add(foldId);
        }
        else
        {
            _collapsedFoldIds.Remove(foldId);
        }

        RenderViewport();
    }

    public void ToggleFold(string foldId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(foldId);
        SetFoldCollapsed(foldId, !_collapsedFoldIds.Contains(foldId));
    }

    public void ExpandAllFolds()
    {
        if (_collapsedFoldIds.Count == 0)
        {
            return;
        }

        _collapsedFoldIds.Clear();
        RenderViewport();
    }

    public new bool Focus(FocusState value) => InputWindow.Focus(value);

    protected override AutomationPeer OnCreateAutomationPeer() =>
        _automationPeer ??= new AzunyanEditorViewAutomationPeer(this);

    private static void OnRenderPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((AzunyanEditorView)sender).RenderViewport();
    }

    private static void OnTextWrappingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (AzunyanEditorView)sender;
        view.InputWindow.NativeTextBoxControl.TextWrapping = (TextWrapping)args.NewValue;
        view.UpdateTextSurfaceMode();
        view.RenderViewport();
    }

    private static void OnTabDisplaySizeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((AzunyanEditorView)sender).RenderViewport();

    private static void OnIndentSizeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((AzunyanEditorView)sender).ApplyIndentSize();

    private static void OnIndentationInputModeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((AzunyanEditorView)sender).ApplyIndentationInputMode();

    private static void OnAcceptsReturnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((AzunyanEditorView)sender).InputWindow.NativeTextBoxControl.AcceptsReturn = (bool)args.NewValue;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        _scrollViewer = FindDescendant<ScrollViewer>(InputWindow.NativeTextBoxControl);
        if (_scrollViewer is not null)
        {
            _scrollViewer.ViewChanged += OnViewportChanged;
        }

        ApplyIndentSize();
        ApplyIndentationInputMode();
        UpdateTextSurfaceMode();
        RenderViewport();
        RequestProviderResults(true, true, true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        _providerScheduler.CancelAll();
        InvalidateProviderGenerations();
        if (_scrollViewer is not null)
        {
            _scrollViewer.ViewChanged -= OnViewportChanged;
            _scrollViewer = null;
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => RenderViewport();

    private void OnInputTextChanged(
        object? sender,
        AzunyanTextInputChangedEventArgs args)
    {
        if (args.Generation != _inputWindowGeneration)
        {
            return;
        }

        if (args.CompositionRange is { } composition)
        {
            _compositionRange = composition;
        }

        _document.Replace(args.Change.OldRange, args.Change.NewText);
        if (_document.Selection != args.Selection)
        {
            _document.Selection = args.Selection;
        }

        SyncInputWindow();
        var documentChange = _pendingProviderDocumentChange;
        _pendingProviderDocumentChange = null;
        RenderViewport();
        if (!IsComposing)
        {
            FlushPendingAutomationDocumentChange(render: false);
            RequestProviderResults(
                true,
                true,
                true,
                requestCompletion: _completionRequested,
                documentChange: documentChange);
        }
    }

    private void OnInputDocumentChanged(
        object? sender,
        DocumentChangedEventArgs args)
    {
        _defaultRenderer.TextRenderer.NotifyDocumentChanged(args);
        _pendingProviderDocumentChange = args;
        _pendingAutomationDocumentChange = args;
        DispatcherQueue.TryEnqueue(FlushPendingAutomationDocumentChange);
        if (!IsComposing
            && !_applyingCompletion
            && InputWindow.FocusState != FocusState.Unfocused)
        {
            var completionWasRequested = _completionRequested || IsCompletionPopupOpen;
            var isEdit = args.Kind == DocumentChangeKind.Edit;
            var isTriggerInsertion = isEdit
                && args.Change.IsInsertion
                && IsCompletionTrigger(args.NewSnapshot.Text, args.NewSelection.CaretPosition);

            if (isTriggerInsertion)
            {
                _explicitCompletionRequested = false;
                _completionRequested = true;
            }
            else if (isEdit && completionWasRequested)
            {
                // Keep the request alive while the user types or deletes a
                // prefix so the provider can refresh and filter the popup.
                // In particular, an explicit Ctrl+Space request must not be
                // dismissed by the first typed character.
                _completionRequested = true;
            }
            else
            {
                _explicitCompletionRequested = false;
                _completionRequested = false;
            }
        }
    }

    private void OnInputCompositionChanged(
        object? sender,
        AzunyanTextInputCompositionChangedEventArgs args)
    {
        if (args.Generation != _inputWindowGeneration)
        {
            return;
        }

        _compositionRange = args.CompositionRange;
        _pendingProviderDocumentChange = null;
        _completionRequested = false;
        _explicitCompletionRequested = false;
        HideCompletionPopup();
        RenderViewport();
        CompositionChanged?.Invoke(this, EventArgs.Empty);
        if (!args.IsComposing)
        {
            FlushPendingAutomationDocumentChange(render: false);

            RequestProviderResults(true, true, true);
        }
    }

    private void OnRendererLayoutInvalidated(object? sender, EventArgs args)
    {
        if (IsLoaded)
        {
            DispatcherQueue.TryEnqueue(RenderViewport);
        }
    }

    private void OnInputSelectionChanged(
        object? sender,
        AzunyanTextInputSelectionChangedEventArgs args)
    {
        if (args.Generation == _inputWindowGeneration)
        {
            _document.Selection = args.Selection;
        }
    }

    private void OnDocumentSelectionChanged(object? sender, EventArgs args)
    {
        SyncInputWindow();
        RenderViewport();
        _automationPeer?.NotifySelectionChanged();
        if (!IsComposing)
        {
            RequestProviderResults(false, false, true, requestCompletion: _completionRequested);
        }
    }

    private void FlushPendingAutomationDocumentChange() =>
        FlushPendingAutomationDocumentChange(render: true);

    private void FlushPendingAutomationDocumentChange(bool render)
    {
        if (IsComposing)
        {
            return;
        }

        var automationChange = _pendingAutomationDocumentChange;
        _pendingAutomationDocumentChange = null;
        if (automationChange is null)
        {
            return;
        }

        if (render)
        {
            RenderViewport();
        }

        _automationPeer?.NotifyDocumentChanged(automationChange);
    }

    private void ReplaceDocumentRangeAndNotify(
        TextRange range,
        string replacement)
    {
        var oldText = Snapshot.Text;
        _document.Replace(range, replacement);
        SyncInputWindow();
        if (string.Equals(oldText, Snapshot.Text, StringComparison.Ordinal))
        {
            return;
        }

        _pendingAutomationDocumentChange = null;
        RenderViewport();
        _automationPeer?.NotifyTextChanged(oldText, Snapshot.Text);
    }

    private void SyncInputWindow()
    {
        if (_synchronizingInputWindow)
        {
            return;
        }

        var currentWindow = InputWindow.WindowRange;
        var window = IsComposing
            && currentWindow.Contains(Document.Selection.Range)
            && _compositionRange is { } composition
            && currentWindow.Contains(composition)
                ? currentWindow
                : _inputWindowCalculator.Calculate(
                    Snapshot,
                    Document.Selection,
                    _compositionRange,
                    currentWindow);
        var generation = checked(++_inputWindowGeneration);

        _synchronizingInputWindow = true;
        try
        {
            InputWindow.SetWindow(
                generation,
                window.Start,
                Snapshot.GetText(window),
                Document.Selection,
                _compositionRange is { } compositionInWindow
                    && window.Contains(compositionInWindow)
                        ? compositionInWindow
                        : null);
        }
        finally
        {
            _synchronizingInputWindow = false;
        }
    }

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (IsCompletionPopupOpen)
        {
            switch (args.Key)
            {
                case VirtualKey.Down:
                    MoveCompletionSelection(1);
                    args.Handled = true;
                    return;
                case VirtualKey.Up:
                    MoveCompletionSelection(-1);
                    args.Handled = true;
                    return;
                case VirtualKey.Enter:
                case VirtualKey.Tab when !IsKeyDown(VirtualKey.Shift):
                    if (TryAcceptSelectedCompletion())
                    {
                        args.Handled = true;
                    }
                    return;
                case VirtualKey.Escape:
                    HideCompletionPopup();
                    _completionRequested = false;
                    _explicitCompletionRequested = false;
                    args.Handled = true;
                    return;
            }
        }

        if (_suppressVerticalCaretNavigation
            && args.Key is VirtualKey.Up or VirtualKey.Down)
        {
            args.Handled = true;
            return;
        }

        var control = IsKeyDown(VirtualKey.Control);
        var menu = IsKeyDown(VirtualKey.Menu);
        var extendSelection = IsKeyDown(VirtualKey.Shift);
        switch (args.Key)
        {
            case VirtualKey.Enter
                when _autoIndentOnEnter
                && AcceptsReturn
                && !IsComposing
                && !control
                && !menu:
                TextEditorCommands.InsertNewLineWithAutoIndent(Document);
                SyncInputWindow();
                args.Handled = true;
                break;
            case VirtualKey.Tab
                when !IsComposing
                && !control
                && !menu:
                TextEditorCommands.IndentSelection(
                    Document,
                    extendSelection,
                    _indentSize,
                    _indentationInputMode);
                SyncInputWindow();
                args.Handled = true;
                break;
            case VirtualKey.Z when control && !IsComposing:
                if (extendSelection)
                {
                    RedoDocument();
                }
                else
                {
                    UndoDocument();
                }

                args.Handled = true;
                break;
            case VirtualKey.Y when control && !IsComposing:
                RedoDocument();
                args.Handled = true;
                break;
            case VirtualKey.Left when !IsComposing:
                Document.MoveCaretByGrapheme(-1, extendSelection);
                SyncInputWindow();
                args.Handled = true;
                break;
            case VirtualKey.Right when !IsComposing:
                Document.MoveCaretByGrapheme(1, extendSelection);
                SyncInputWindow();
                args.Handled = true;
                break;
            case VirtualKey.Delete when !IsComposing:
                Document.DeleteForward();
                SyncInputWindow();
                args.Handled = true;
                break;
        }

    }

    private void OnInputFocusChanged(object? sender, EventArgs args) =>
        _automationPeer?.NotifyFocusChanged();

    private void OnCompletionListKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!IsCompletionPopupOpen)
        {
            return;
        }

        switch (args.Key)
        {
            case VirtualKey.Down:
                if (!args.Handled)
                {
                    MoveCompletionSelection(1);
                }

                args.Handled = true;
                return;
            case VirtualKey.Up:
                if (!args.Handled)
                {
                    MoveCompletionSelection(-1);
                }

                args.Handled = true;
                return;
            case VirtualKey.Enter:
            case VirtualKey.Tab when !IsKeyDown(VirtualKey.Shift):
                if (TryAcceptSelectedCompletion())
                {
                    args.Handled = true;
                }

                return;
            case VirtualKey.Escape:
                HideCompletionPopup();
                _completionRequested = false;
                _explicitCompletionRequested = false;
                args.Handled = true;
                return;
        }
    }

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(CoreVirtualKeyStates.Down);

    private void OnInputPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (!IsProjectedTextSurface)
        {
            return;
        }

        _completionRequested = false;
        HideCompletionPopup();
        _hoverPosition = -1;
        HideTooltipPopup();

        var point = args.GetCurrentPoint(EditorPointerSurface);
        if (point.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse
            && !point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (!_defaultRenderer.TextRenderer.TryHitTest(
                point.Position.X,
                point.Position.Y,
                InputWindow.NativeTextBoxControl.Padding.Left,
                InputWindow.NativeTextBoxControl.Padding.Top,
                _scrollViewer?.HorizontalOffset ?? 0,
                GetVerticalOffset(),
                _characterWidth,
                out var anchor,
                out var foldId,
                out var adornmentId))
        {
            return;
        }

        if (foldId is not null)
        {
            ToggleFold(foldId);
        }
        else if (adornmentId is not null
            && TryGetAdornment(adornmentId, out var adornment))
        {
            SetDocumentSelection(TextSelection.Caret(anchor.Position.Offset));
            AdornmentInvoked?.Invoke(
                this,
                new AdornmentInvokedEventArgs(
                    adornment.Id,
                    adornment.Kind,
                    adornment.Anchor,
                    adornment.Content,
                    adornment.IsBlock));
        }
        else
        {
            SetDocumentSelection(TextSelection.Caret(anchor.Position.Offset));
        }

        args.Handled = true;
    }

    private bool TryGetAdornment(
        string id,
        out (string Id, string Kind, DocumentAnchor Anchor, AdornmentContent Content, bool IsBlock) adornment)
    {
        var viewport = GetCurrentFrame()?.Viewport;
        var inlay = viewport?.Inlays.FirstOrDefault(item => item.Id == id);
        if (inlay is not null)
        {
            adornment = (inlay.Id, inlay.Kind, inlay.Anchor, inlay.Content, false);
            return true;
        }

        var block = viewport?.BlockAdornments.FirstOrDefault(item => item.Id == id);
        if (block is not null)
        {
            adornment = (block.Id, block.Kind, block.Anchor, block.Content, true);
            return true;
        }

        adornment = default;
        return false;
    }

    private void OnInputPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (!IsProjectedTextSurface)
        {
            return;
        }

        var point = args.GetCurrentPoint(EditorPointerSurface);
        if (!_defaultRenderer.TextRenderer.TryHitTest(
                point.Position.X,
                point.Position.Y,
                InputWindow.NativeTextBoxControl.Padding.Left,
                InputWindow.NativeTextBoxControl.Padding.Top,
                _scrollViewer?.HorizontalOffset ?? 0,
                GetVerticalOffset(),
                _characterWidth,
                out var anchor,
                out _))
        {
            OnInputPointerExited(sender, args);
            return;
        }

        var position = anchor.Position.Offset;
        if (_hoverPosition == position)
        {
            return;
        }

        _hoverPosition = position;
        _completionRequested = false;
        HideCompletionPopup();
        HideTooltipPopup();
        RequestHoverProvider(position);
    }

    private void OnInputPointerExited(object sender, PointerRoutedEventArgs args)
    {
        _hoverPosition = -1;
        HideTooltipPopup();
    }

    private void OnViewportChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (IsProjectedTextSurface && !_synchronizingProjectedScroll)
        {
            _projectedVerticalOffset = _scrollViewer?.VerticalOffset ?? 0;
        }

        RenderViewport();
        RequestProviderResults(false, true, false);
    }

    internal IReadOnlyList<IRawElementProviderSimple> GetProjectedAutomationChildren(
        TextRange range)
    {
        if (!IsProjectedTextSurface)
        {
            return Array.Empty<IRawElementProviderSimple>();
        }

        var providers = new List<IRawElementProviderSimple>();
        foreach (var child in RenderOverlay.Children)
        {
            if (child is not FrameworkElement element
                || element.Tag is not ProjectedTextAutomationTarget target
                || !IsAutomationTargetInRange(target, range))
            {
                continue;
            }

            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(element);
            if (peer is not null
                && AzunyanEditorViewAutomationPeer.GetRawProvider(peer)
                    is { } provider)
            {
                providers.Add(provider);
            }
        }

        return providers;
    }

    internal bool TryGetProjectedAutomationChildRange(
        IRawElementProviderSimple child,
        out TextRange range)
    {
        range = default;
        if (!IsProjectedTextSurface)
        {
            return false;
        }

        foreach (var element in RenderOverlay.Children)
        {
            if (element is not FrameworkElement frameworkElement
                || frameworkElement.Tag is not ProjectedTextAutomationTarget target)
            {
                continue;
            }

            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(frameworkElement);
            var provider = peer is null
                ? null
                : AzunyanEditorViewAutomationPeer.GetRawProvider(peer);
            if (provider is not null && provider.Equals(child))
            {
                range = target.Range;
                return true;
            }
        }

        return false;
    }

    private void CreateProjectedAutomationChildren()
    {
        if (!IsProjectedTextSurface)
        {
            if (_projectedAutomationStructureKey.Length > 0)
            {
                _projectedAutomationStructureKey = string.Empty;
                _automationPeer?.NotifyStructureChanged();
            }

            return;
        }

        var targets = _defaultRenderer.TextRenderer.GetAutomationTargets();
        var structureKey = string.Join(
            '\u001f',
            targets.Select(target => $"{target.Kind}\u001f{target.Id}"));
        var structureChanged = !string.Equals(
            _projectedAutomationStructureKey,
            structureKey,
            StringComparison.Ordinal);
        _projectedAutomationStructureKey = structureKey;

        foreach (var target in targets)
        {
            var button = new ProjectedTextAutomationButton
            {
                Width = Math.Max(1, target.Bounds.Width),
                Height = Math.Max(1, target.Bounds.Height),
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                IsTabStop = false,
                IsHitTestVisible = false,
                Tag = target
            };
            AutomationProperties.SetName(button, target.Name);
            AutomationProperties.SetAutomationId(
                button,
                $"azunyan-{target.Kind}-{target.Id}");
            AutomationProperties.SetHelpText(
                button,
                target.IsFold ? "Collapsed code region" : target.Kind);
            button.Click += OnProjectedAutomationChildClick;
            Canvas.SetLeft(button, target.Bounds.X);
            Canvas.SetTop(button, target.Bounds.Y);
            RenderOverlay.Children.Add(button);
        }

        if (structureChanged)
        {
            _automationPeer?.NotifyStructureChanged();
        }
    }

    private void OnProjectedAutomationChildClick(
        object sender,
        RoutedEventArgs args)
    {
        if (sender is not FrameworkElement element
            || element.Tag is not ProjectedTextAutomationTarget target)
        {
            return;
        }

        if (target.IsFold)
        {
            ToggleFold(target.Id);
            return;
        }

        if (!TryGetAdornment(target.Id, out var adornment))
        {
            return;
        }

        SetDocumentSelection(
            TextSelection.Caret(adornment.Anchor.Position.Offset));
        AdornmentInvoked?.Invoke(
            this,
            new AdornmentInvokedEventArgs(
                adornment.Id,
                adornment.Kind,
                adornment.Anchor,
                adornment.Content,
                adornment.IsBlock));
    }

    private static bool IsAutomationTargetInRange(
        ProjectedTextAutomationTarget target,
        TextRange range)
    {
        if (target.Range.IsEmpty)
        {
            var position = target.Anchor.Position.Offset;
            return position >= range.Start && position <= range.End;
        }

        return target.Range.End > range.Start
            && target.Range.Start < range.End;
    }

    private TextRange GetVisibleDocumentRange()
    {
        UpdateTextMetrics();
        var snapshot = Snapshot;
        if (IsProjectedTextSurface
            && _defaultRenderer.TextRenderer.TryGetVisibleDocumentRange(
                out var projectedRange))
        {
            return projectedRange;
        }

        var lines = snapshot.Lines;
        var lineCount = lines.LineCount;
        if (TextWrapping == TextWrapping.Wrap)
        {
            return TextRange.FromBounds(0, snapshot.Length);
        }

        var verticalOffset = GetVerticalOffset();
        var viewportHeight = Math.Max(1, ProjectedSurfaceHost.ActualHeight);
        var first = Math.Clamp(
            (int)Math.Floor(verticalOffset / _lineHeight) - 2,
            0,
            Math.Max(0, lineCount - 1));
        var last = Math.Clamp(
            (int)Math.Ceiling((verticalOffset + viewportHeight) / _lineHeight) + 2,
            0,
            Math.Max(0, lineCount - 1));
        return TextRange.FromBounds(lines.GetLineRange(first).Start, lines.GetLineRange(last).End);
    }

    internal bool IsProjectedTextSurfaceActive => IsProjectedTextSurface;

    internal TextSelection AutomationSelection => Document.Selection;

    internal bool TryGetProjectedVisibleDocumentRange(out TextRange range)
    {
        range = default;
        return IsProjectedTextSurface
            && _defaultRenderer.TextRenderer.TryGetVisibleDocumentRange(out range);
    }

    internal bool TryGetProjectedRangeRectangles(
        TextRange range,
        out IReadOnlyList<Rect> rectangles)
    {
        rectangles = Array.Empty<Rect>();
        if (!IsProjectedTextSurface
            || !_defaultRenderer.TextRenderer.TryGetRangeRectangles(
                range,
                out var localRectangles))
        {
            return false;
        }

        try
        {
            var transform = ProjectedSurfaceHost.TransformToVisual(null);
            rectangles = localRectangles
                .Select(rect =>
                {
                    var topLeft = transform.TransformPoint(new Point(rect.X, rect.Y));
                    var bottomRight = transform.TransformPoint(
                        new Point(rect.X + rect.Width, rect.Y + rect.Height));
                    return new Rect(
                        topLeft.X,
                        topLeft.Y,
                        Math.Max(0, bottomRight.X - topLeft.X),
                        Math.Max(0, bottomRight.Y - topLeft.Y));
                })
                .ToArray();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal bool TryGetProjectedRangeFromPoint(
        Point screenPoint,
        out TextRange range)
    {
        range = default;
        if (!IsProjectedTextSurface)
        {
            return false;
        }

        try
        {
            var transform = ProjectedSurfaceHost.TransformToVisual(null);
            var localPoint = transform.Inverse.TransformPoint(screenPoint);
            if (!_defaultRenderer.TextRenderer.TryHitTest(
                localPoint.X,
                localPoint.Y,
                InputWindow.NativeTextBoxControl.Padding.Left,
                InputWindow.NativeTextBoxControl.Padding.Top,
                _scrollViewer?.HorizontalOffset ?? 0,
                GetVerticalOffset(),
                _characterWidth,
                out var anchor,
                out _,
                out _))
            {
                return false;
            }

            range = TextRange.Empty(anchor.Position.Offset);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal bool ScrollProjectedRangeIntoView(TextRange range, bool alignToTop)
    {
        if (!IsProjectedTextSurface
            || range.Start < 0
            || range.End > Snapshot.Length
            || !_defaultRenderer.TextRenderer.TryGetViewportOffset(
                DocumentAnchor.Before(range.Start),
                0,
                out var startOffset))
        {
            return false;
        }

        var targetOffset = startOffset;
        if (!alignToTop
            && _defaultRenderer.TextRenderer.TryGetViewportOffset(
                DocumentAnchor.Before(range.End),
                0,
                out var endOffset))
        {
            targetOffset = endOffset
                - Math.Max(1, EditorHost.ActualHeight)
                + _lineHeight;
        }

        var maximum = Math.Max(
            0,
            _defaultRenderer.TextRenderer.ContentHeight
                - Math.Max(1, EditorHost.ActualHeight));
        targetOffset = Math.Clamp(targetOffset, 0, maximum);
        _synchronizingProjectedScroll = true;
        try
        {
            _projectedVerticalOffset = targetOffset;
            ProjectedVerticalScrollBar.Value = targetOffset;
            _scrollViewer?.ChangeView(
                horizontalOffset: null,
                verticalOffset: targetOffset,
                zoomFactor: null,
                disableAnimation: true);
        }
        finally
        {
            _synchronizingProjectedScroll = false;
        }

        RenderViewport();
        return true;
    }

    private void UpdateTextMetrics()
    {
        var sample = new TextBlock
        {
            Text = "M",
            FontFamily = InputWindow.NativeTextBoxControl.FontFamily,
            FontSize = InputWindow.NativeTextBoxControl.FontSize
        };
        sample.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _lineHeight = Math.Max(1, sample.DesiredSize.Height);

        sample.Text = "0";
        sample.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _characterWidth = Math.Max(1, sample.DesiredSize.Width);
    }

    private void ApplyIndentSize() => _indentSize = IndentSize;

    private void ApplyIndentationInputMode() =>
        _indentationInputMode = IndentationInputMode;

    private void ApplyColorScheme()
    {
        RootGrid.Background = new SolidColorBrush(_colorScheme.EditorBackground);
        InputWindow.NativeTextBoxControl.Background =
            new SolidColorBrush(_colorScheme.EditorBackground);
        GutterCanvas.Background = new SolidColorBrush(_colorScheme.GutterBackground);

        CompletionBorder.Background = new SolidColorBrush(_colorScheme.PopupBackground);
        CompletionBorder.BorderBrush = new SolidColorBrush(_colorScheme.PopupBorder);
        CompletionList.Foreground = new SolidColorBrush(_colorScheme.PopupForeground);
        CompletionDetailsBorder.Background = new SolidColorBrush(_colorScheme.PopupBackground);
        CompletionDetailsBorder.BorderBrush = new SolidColorBrush(_colorScheme.PopupBorder);
        CompletionDetailsTitle.Foreground = new SolidColorBrush(_colorScheme.PopupForeground);
        CompletionDetailsContent.Foreground = new SolidColorBrush(_colorScheme.PopupForeground);

        TooltipBorder.Background = new SolidColorBrush(_colorScheme.TooltipBackground);
        TooltipBorder.BorderBrush = new SolidColorBrush(_colorScheme.TooltipBorder);
        TooltipTitle.Foreground = new SolidColorBrush(_colorScheme.TooltipForeground);
        TooltipContent.Foreground = new SolidColorBrush(_colorScheme.TooltipForeground);
    }

    private void UpdateTextSurfaceMode()
    {
        if (IsProjectedTextSurface)
        {
            // TextBox remains the native editing/IME host, but its template
            // must not paint a second copy of the document. Zero opacity on
            // the control hides its whole template subtree, including the
            // selection highlight the inner ScrollViewer composes. The
            // projected layers occupy the same EditorHost cell above it.
            var nativeTextBox = InputWindow.NativeTextBoxControl;
            nativeTextBox.Opacity = 0;
            nativeTextBox.Foreground = new SolidColorBrush(Colors.Transparent);
            nativeTextBox.SelectionHighlightColor = new SolidColorBrush(Colors.Transparent);
            nativeTextBox.SelectionHighlightColorWhenNotFocused = new SolidColorBrush(Colors.Transparent);
            GutterDrawingSurface.Visibility = Visibility.Visible;
            TextDrawingSurface.Visibility = Visibility.Visible;
            ScrollViewer.SetHorizontalScrollBarVisibility(nativeTextBox, ScrollBarVisibility.Hidden);
            ScrollViewer.SetVerticalScrollBarVisibility(nativeTextBox, ScrollBarVisibility.Hidden);
            ProjectedVerticalScrollBar.Visibility = Visibility.Visible;
            return;
        }

        var fallbackTextBox = InputWindow.NativeTextBoxControl;
        fallbackTextBox.Opacity = 1;
        fallbackTextBox.ClearValue(Control.ForegroundProperty);
        fallbackTextBox.ClearValue(TextBox.SelectionHighlightColorProperty);
        fallbackTextBox.ClearValue(TextBox.SelectionHighlightColorWhenNotFocusedProperty);
        GutterDrawingSurface.Visibility = Visibility.Collapsed;
        TextDrawingSurface.Visibility = Visibility.Collapsed;
        ScrollViewer.SetHorizontalScrollBarVisibility(fallbackTextBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(fallbackTextBox, ScrollBarVisibility.Auto);
        ProjectedVerticalScrollBar.Visibility = Visibility.Collapsed;
        _projectedVerticalOffset = 0;
        _completionRequested = false;
        HideCompletionPopup();
        _hoverPosition = -1;
        HideTooltipPopup();
    }

    private bool IsProjectedTextSurface =>
        ReferenceEquals(_renderer, _defaultRenderer);

    private double GetVerticalOffset() => IsProjectedTextSurface
        ? _projectedVerticalOffset
        : _scrollViewer?.VerticalOffset ?? 0;

    private void OnProjectedVerticalScrollChanged(
        object sender,
        RangeBaseValueChangedEventArgs args)
    {
        if (!IsProjectedTextSurface || _synchronizingProjectedScroll)
        {
            return;
        }

        _projectedVerticalOffset = args.NewValue;
        _synchronizingProjectedScroll = true;
        try
        {
            _scrollViewer?.ChangeView(
                horizontalOffset: null,
                verticalOffset: _projectedVerticalOffset,
                zoomFactor: null,
                disableAnimation: true);
        }
        finally
        {
            _synchronizingProjectedScroll = false;
        }

        RenderViewport();
        RequestProviderResults(false, true, false);
    }

    private void UpdateProjectedScrollExtent(double viewportHeight)
    {
        if (!IsProjectedTextSurface)
        {
            return;
        }

        var contentHeight = _defaultRenderer.TextRenderer.ContentHeight;
        var maximum = Math.Max(0, contentHeight - viewportHeight);
        var offset = Math.Clamp(_projectedVerticalOffset, 0, maximum);
        _synchronizingProjectedScroll = true;
        try
        {
            ProjectedVerticalScrollBar.ViewportSize = viewportHeight;
            ProjectedVerticalScrollBar.Maximum = maximum;
            ProjectedVerticalScrollBar.LargeChange = Math.Max(1, viewportHeight);
            ProjectedVerticalScrollBar.SmallChange = Math.Max(1, _lineHeight);
            ProjectedVerticalScrollBar.Value = offset;
            _projectedVerticalOffset = offset;
            if (_scrollViewer is { } scrollViewer
                && Math.Abs(scrollViewer.VerticalOffset - offset) > 0.5)
            {
                scrollViewer.ChangeView(
                    horizontalOffset: null,
                    verticalOffset: offset,
                    zoomFactor: null,
                    disableAnimation: true);
            }
        }
        finally
        {
            _synchronizingProjectedScroll = false;
        }
    }

    private bool IsCompletionPopupOpen => CompletionPopup.IsOpen;

}
