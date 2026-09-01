using Azunyan.Core;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
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
using Windows.ApplicationModel.DataTransfer;
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
    private const VirtualKey OemOpenBracketKey = (VirtualKey)0xdb;

    private readonly AzunyanEditorRenderer _defaultRenderer;
    private Document _document = new();
    private readonly SlidingInputWindowCalculator _inputWindowCalculator = new();
    private TextRange? _compositionRange;
    private long _inputWindowGeneration;
    private bool _synchronizingInputWindow;
    private bool _applyingDocumentCommand;
    private bool _applyingInputChange;
    private bool _inputWindowSynchronizationPending;
    private bool _inputWindowSynchronizationScheduled;
    private readonly HashSet<VirtualKey> _nativeKeysDown = new();
    private bool _autoIndentOnEnter = true;
    private bool _suppressVerticalCaretNavigation;
    private int? _indentSize;
    private IndentationInputMode _indentationInputMode;
    private string? _preferredLineEnding;
    private readonly Queue<Action> _pendingCompositionOperations = new();
    private readonly HashSet<VirtualKey> _queuedNonRepeatingKeys = new();
    private long _keyInputEpoch;
    private readonly Queue<Func<Task>> _pendingPasteOperations = new();
    private bool _pasteOperationRunning;
    private bool _pasteBatchActive;
    private bool _pasteBatchRefreshPending;
    private bool _pasteBatchRefreshScheduled;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _pasteBatchRefreshTimer;
    private long _pasteOperationSequence;
    private DateTimeOffset _clipboardUnavailableUntil;
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
    private TextBlockSelection? _blockSelection;
    private int? _pointerSelectionAnchor;
    private TextBlockPosition? _pointerBlockSelectionAnchor;
    private bool _pointerSelectingBlock;
    private bool _pointerRenderScheduled;
    private uint? _selectionPointerId;
    private string _projectedAutomationStructureKey = string.Empty;
    private long _documentProviderGeneration;
    private long _viewportProviderGeneration;
    private long _positionProviderGeneration;
    private DocumentChangedEventArgs? _pendingProviderDocumentChange;
    private DocumentChangedEventArgs? _pendingAutomationDocumentChange;
    private AzunyanEditorViewAutomationPeer? _automationPeer;
    private long _diagnosticOperationSequence;
    private string? _diagnosticOperation;
    private string? _diagnosticPasteOperation;

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
        _document.CaretSetChanged += OnDocumentCaretSetChanged;
        InputWindow.ExceptionSink = OnNativeInputCallbackException;
        InputWindow.NativeTextBoxControl.Padding = new Thickness(8, 6, 8, 6);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ProjectedSurfaceHost.SizeChanged += OnSizeChanged;
        InputWindow.InputChanged += OnInputTextChanged;
        InputWindow.InputSelectionChanged += OnInputSelectionChanged;
        InputWindow.CompositionChanged += OnInputCompositionChanged;
        InputWindow.NativeFocusChanged += OnInputFocusChanged;
        InputWindow.NativeTextBoxControl.BeforeKeyDown += OnInputKeyDown;
        InputWindow.NativeTextBoxControl.AfterKeyUp += OnInputKeyUp;
        EditorPointerSurface.PointerPressed += OnInputPointerPressed;
        EditorPointerSurface.PointerReleased += OnInputPointerReleased;
        EditorPointerSurface.PointerCanceled += OnInputPointerCanceled;
        EditorPointerSurface.PointerCaptureLost += OnInputPointerCaptureLost;
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
        if (_pasteBatchRefreshTimer is { } pasteBatchRefreshTimer)
        {
            pasteBatchRefreshTimer.Stop();
            pasteBatchRefreshTimer.Tick -= OnPasteBatchRefreshTimerTick;
        }

        _pasteBatchRefreshTimer = null;
        InputWindow.NativeTextBoxControl.BeforeKeyDown -= OnInputKeyDown;
        InputWindow.NativeTextBoxControl.AfterKeyUp -= OnInputKeyUp;
        EditorPointerSurface.PointerReleased -= OnInputPointerReleased;
        EditorPointerSurface.PointerCanceled -= OnInputPointerCanceled;
        EditorPointerSurface.PointerCaptureLost -= OnInputPointerCaptureLost;
        StopPointerSelection();
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

    public void SetFontFamily(string fontFamily)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFamily);
        var family = new FontFamily(fontFamily.Trim());
        FontFamily = family;
        InputWindow.NativeTextBoxControl.FontFamily = family;
        UpdateTextMetrics();
        RenderViewport();
    }

    public void SetFontSize(double fontSize)
    {
        if (!double.IsFinite(fontSize) || fontSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        }

        FontSize = fontSize;
        InputWindow.NativeTextBoxControl.FontSize = fontSize;
        UpdateTextMetrics();
        RenderViewport();
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

    /// <summary>
    /// Categories enabled for the detailed operation log. The default is
    /// <see cref="AzunyanDiagnosticCategory.None"/>, so the high-volume
    /// diagnostic path is disabled until the host opts in.
    /// </summary>
    public AzunyanDiagnosticCategory DiagnosticCategories { get; set; }

    /// <summary>
    /// Receives detailed diagnostics for enabled categories. The host may
    /// connect this to its crash log.
    /// </summary>
    public Action<AzunyanDiagnosticCategory, string>? DiagnosticSink { get; set; }

    /// <summary>
    /// Receives caught callback and operation exceptions independently of the
    /// detailed operation-log category filter.
    /// </summary>
    public Action<string, Exception>? DiagnosticExceptionSink { get; set; }

    internal TextBox InputHost => InputWindow.NativeTextBoxControl;

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
        get => _blockSelection is { } block
            ? GetBlockSelectionText(block)
            : Snapshot.GetText(Document.Selection.Range);
        set
        {
            if (_blockSelection is { } block)
            {
                RunAfterComposition(() =>
                {
                    ApplyDocumentCommand(() => ApplyBlockReplacement(
                        block,
                        new[] { value },
                        repeatSingleLine: true));
                });
                return;
            }

            ReplaceDocumentRange(Document.Selection.Range, value);
        }
    }

    public TextBlockSelection? RectangularSelection => _blockSelection;

    public TextCaretSet? VirtualCarets => Document.CaretSet.Count > 1
        ? Document.CaretSet
        : null;

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

    public void SetPreferredLineEnding(string? lineEnding)
    {
        if (lineEnding is not null
            && lineEnding is not "\n" and not "\r\n" and not "\r")
        {
            throw new ArgumentException(
                "The preferred line ending must be LF, CRLF, or CR.",
                nameof(lineEnding));
        }

        _preferredLineEnding = lineEnding;
        InputWindow.PreferredLineEnding = lineEnding;
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
            ArgumentNullException.ThrowIfNull(value);
            _renderer = value;
            UpdateTextSurfaceMode();
            RenderViewport();
        }
    }

    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        RunAfterComposition(() => SetTextCore(text));
    }

    private void SetTextCore(string text)
    {
        var oldText = Snapshot.Text;
        _document.Changed -= OnInputDocumentChanged;
        _document.SelectionChanged -= OnDocumentSelectionChanged;
        _document.CaretSetChanged -= OnDocumentCaretSetChanged;
        _compositionRange = null;
        _blockSelection = null;
        _document = new Document(text);
        _document.Changed += OnInputDocumentChanged;
        _document.SelectionChanged += OnDocumentSelectionChanged;
        _document.CaretSetChanged += OnDocumentCaretSetChanged;
        SyncInputWindow();
        RenderViewport();
        _automationPeer?.NotifyTextChanged(oldText, Snapshot.Text);
    }

    public void SetDocumentSelection(TextSelection selection)
    {
        RunAfterComposition(() =>
        {
            var hadBlockSelection = _blockSelection is not null;
            _blockSelection = null;
            _document.Selection = selection;
            if (_selectionPointerId is not null)
            {
                RequestPointerRender();
                return;
            }

            SyncInputWindow();
            if (hadBlockSelection)
            {
                RenderViewport();
            }
        });
    }

    public void ScrollSelectionIntoView()
    {
        RunAfterComposition(() =>
        {
            if (IsProjectedTextSurface)
            {
                ScrollProjectedRangeIntoView(
                    TextRange.Empty(Document.Selection.CaretPosition),
                    alignToTop: false);
            }
        });
    }

    public void ReplaceDocumentRange(TextRange range, string replacement) =>
        RunAfterComposition(() => ReplaceDocumentRangeAndNotify(range, replacement));

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
        if (IsComposing)
        {
            RunAfterComposition(() => UndoDocumentCore());
            return false;
        }

        return UndoDocumentCore();
    }

    private bool UndoDocumentCore()
    {
        var hadBlockSelection = _blockSelection is not null;
        _blockSelection = null;
        var result = _document.Undo();
        if (result)
        {
            SyncInputWindow();
        }
        else if (hadBlockSelection)
        {
            RenderViewport();
        }

        return result;
    }

    public bool RedoDocument()
    {
        if (IsComposing)
        {
            RunAfterComposition(() => RedoDocumentCore());
            return false;
        }

        return RedoDocumentCore();
    }

    private bool RedoDocumentCore()
    {
        var hadBlockSelection = _blockSelection is not null;
        _blockSelection = null;
        var result = _document.Redo();
        if (result)
        {
            SyncInputWindow();
        }
        else if (hadBlockSelection)
        {
            RenderViewport();
        }

        return result;
    }

    public void CutSelectionToClipboard()
    {
        LogDiagnostic(
            AzunyanDiagnosticCategory.Clipboard,
            $"cut-request; {DescribeDiagnosticState()}");
        RunAfterComposition(CutSelectionToClipboardCore);
    }

    private void CutSelectionToClipboardCore()
    {
        if (_blockSelection is { } blockSelection)
        {
            SetClipboardText(GetBlockSelectionText(blockSelection));
            ApplyDocumentCommand(() => ApplyBlockReplacement(
                blockSelection,
                new[] { string.Empty },
                repeatSingleLine: true));
            return;
        }

        if (Document.CaretSet.Count > 1)
        {
            SetClipboardText(GetCaretSetSelectedText());
            ApplyDocumentCommand(() => ApplyCaretSetDeletion());
            return;
        }

        if (Document.Selection.Length > 0)
        {
            SetClipboardText(Snapshot.GetText(Document.Selection.Range));
            ApplyDocumentCommand(() => Document.DeleteSelection());
        }
    }

    public void CopySelectionToClipboard()
    {
        LogDiagnostic(
            AzunyanDiagnosticCategory.Clipboard,
            $"copy-request; {DescribeDiagnosticState()}");
        if (_blockSelection is { } blockSelection)
        {
            SetClipboardText(GetBlockSelectionText(blockSelection));
            return;
        }

        if (Document.CaretSet.Count > 1)
        {
            SetClipboardText(GetCaretSetSelectedText());
            return;
        }

        if (Document.Selection.Length > 0)
        {
            SetClipboardText(Snapshot.GetText(Document.Selection.Range));
        }
    }

    public void PasteFromClipboard()
    {
        RunAfterComposition(() => QueuePasteOperation(PasteFromClipboardAsync));
    }

    private void QueuePasteOperation(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        _pendingPasteOperations.Enqueue(operation);
        LogDiagnostic(
            AzunyanDiagnosticCategory.Clipboard,
            $"queued-paste pending={_pendingPasteOperations.Count}; {DescribeDiagnosticState()}");
        if (!_pasteOperationRunning)
        {
            _ = DrainPasteOperationsAsync();
        }
    }

    private async Task DrainPasteOperationsAsync()
    {
        if (_pasteOperationRunning)
        {
            return;
        }

        _pasteOperationRunning = true;
        _pasteBatchActive = true;
        try
        {
            while (!_disposed && _pendingPasteOperations.Count > 0)
            {
                var operation = _pendingPasteOperations.Dequeue();
                var operationId = $"paste-{Interlocked.Increment(ref _pasteOperationSequence)}";
                LogDiagnostic(
                    AzunyanDiagnosticCategory.Clipboard,
                    $"BEGIN {operationId}; pending={_pendingPasteOperations.Count}; "
                    + DescribeDiagnosticState());
                var previousPasteDiagnosticOperation = _diagnosticPasteOperation;
                _diagnosticPasteOperation = operationId;
                try
                {
                    await operation();
                    LogDiagnostic(
                        AzunyanDiagnosticCategory.Clipboard,
                        $"END {operationId}; pending={_pendingPasteOperations.Count}; "
                        + DescribeDiagnosticState());
                }
                catch (Exception exception)
                {
                    ReportDiagnosticException($"Paste/{operationId}", exception);
                    LogDiagnostic(
                        AzunyanDiagnosticCategory.Clipboard,
                        $"FAILED {operationId}; {exception}");
                }
                finally
                {
                    _diagnosticPasteOperation = previousPasteDiagnosticOperation;
                }
            }
        }
        finally
        {
            _pasteBatchActive = false;
            _pasteOperationRunning = false;
            RequestPendingPasteBatchRefresh();
            if (!_disposed && _pendingPasteOperations.Count > 0)
            {
                _ = DrainPasteOperationsAsync();
            }
        }
    }

    public void SelectAll()
    {
        RunAfterComposition(() =>
        {
            var hadBlockSelection = _blockSelection is not null;
            _blockSelection = null;
            _document.Select(TextRange.FromBounds(0, Snapshot.Length));
            SyncInputWindow();
            if (hadBlockSelection)
            {
                RenderViewport();
            }
        });
    }

    public void Select(int start, int length)
    {
        RunAfterComposition(() =>
        {
            var hadBlockSelection = _blockSelection is not null;
            _blockSelection = null;
            _document.Select(new TextRange(start, length));
            SyncInputWindow();
            if (hadBlockSelection)
            {
                RenderViewport();
            }
        });
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
        if ((TextWrapping)args.OldValue != (TextWrapping)args.NewValue)
        {
            view._blockSelection = null;
            view.StopPointerSelection();
            if ((TextWrapping)args.NewValue != TextWrapping.NoWrap
                && view.Document.CaretSet.Count > 1)
            {
                view._document.Selection = view.Document.Selection;
            }
        }

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

    private static void OnAcceptsReturnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((AzunyanEditorView)sender).InputWindow.NativeTextBoxControl.AcceptsReturn = (bool)args.NewValue;

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
        // The document workflow may request focus before the native sliding
        // window has completed its first layout. Re-apply it on the next UI
        // turn so real keyboard input reaches the inner TextBox, not only the
        // projected editor automation peer.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_disposed && IsLoaded)
            {
                InputWindow.Focus(FocusState.Programmatic);
            }
        });
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        _providerScheduler.CancelAll();
        InvalidateProviderGenerations();
        if (_scrollViewer is not null && !IsProjectedTextSurface)
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
            LogDiagnostic(
                AzunyanDiagnosticCategory.Input,
                $"native-text-changed-stale generation={args.Generation}; currentGeneration={_inputWindowGeneration}");
            return;
        }

        LogDiagnostic(
            AzunyanDiagnosticCategory.Input,
            $"native-text-changed generation={args.Generation}; oldRange={args.Change.OldRange}; "
            + $"newTextLength={args.Change.NewText.Length}; selection={args.Selection}; "
            + $"composition={args.CompositionRange?.ToString() ?? "none"}; {DescribeDiagnosticState()}");
        var oldSnapshot = Snapshot;
        _applyingInputChange = true;
        try
        {
            if (_blockSelection is { } blockSelection)
            {
                var inputLines = SplitBlockInput(args.Change.NewText);
                ApplyBlockReplacement(
                    blockSelection,
                    inputLines,
                    repeatSingleLine: inputLines.Length == 1);
            }
            else if (Document.CaretSet.Count > 1
                && args.CompositionRange is null)
            {
                ApplyCaretSetReplacement(args.Change.NewText);
            }
            else if (Document.CaretSet.Count > 1
                && args.CompositionRange is { })
            {
                _compositionRange = args.CompositionRange;
                ApplyPrimaryCaretSetReplacement(args.Change, args.Selection);
            }
            else
            {
                if (args.CompositionRange is { } composition)
                {
                    _compositionRange = composition;
                }

                _document.Replace(args.Change.OldRange, args.Change.NewText);
                if (_document.Selection != args.Selection)
                {
                    _document.Selection = args.Selection;
                }

                if (_autoIndentOnEnter
                    && args.Change.NewText.Any(character => character is '\r' or '\n'))
                {
                    var autoIndentedBreak = TextEditorCommands.GetNewLineWithAutoIndentation(
                        oldSnapshot,
                        args.Change.OldRange.Start,
                        _preferredLineEnding);
                    if (TryGetLeadingLineEndingLength(
                            autoIndentedBreak,
                            out var generatedLineEndingLength)
                        && autoIndentedBreak.Length > generatedLineEndingLength)
                    {
                        var indentation = autoIndentedBreak[generatedLineEndingLength..];
                        _document.Replace(
                            TextRange.Empty(_document.Selection.CaretPosition),
                            indentation);
                    }
                }
            }
        }
        finally
        {
            _applyingInputChange = false;
        }

        var documentChange = _pendingProviderDocumentChange;
        _pendingProviderDocumentChange = null;
        RenderViewport();
        _inputWindowSynchronizationPending = !IsInputWindowSynchronized();
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

        if (_nativeKeysDown.Count == 0)
        {
            RequestInputWindowSynchronization();
        }
    }

    private void ApplyBlockReplacement(
        TextBlockSelection selection,
        IReadOnlyList<string> replacementLines,
        bool repeatSingleLine)
    {
        if (selection.CoordinateSpace == TextBlockSelectionCoordinateSpace.VisualRows)
        {
            if (!TryCreateBlockSelectionCaretSet(selection, out var caretSet))
            {
                _blockSelection = null;
                return;
            }

            _blockSelection = null;
            ApplyVisualBlockReplacement(caretSet, replacementLines);
            return;
        }

        var edit = TextBlockSelectionOperations.CreateReplacement(
            Snapshot,
            selection,
            TabDisplaySize,
            replacementLines,
            repeatSingleLine,
            TextBlockSelectionOperations.GetPreferredLineEnding(
                Snapshot,
                _preferredLineEnding));
        _blockSelection = null;
        if (edit is not { } blockEdit)
        {
            return;
        }

        var currentText = Snapshot.Text;
        var replacedText = currentText[..blockEdit.Range.Start]
            + blockEdit.Replacement
            + currentText[blockEdit.Range.End..];
        var replacedSnapshot = new TextSnapshot(replacedText);
        var caretPosition = TextBlockSelectionOperations.GetCaretPosition(
            replacedSnapshot,
            selection.Active,
            TabDisplaySize);
        _document.Replace(
            blockEdit.Range,
            blockEdit.Replacement,
            TextSelection.Caret(caretPosition));
    }

    private void ApplyVisualBlockReplacement(
        TextCaretSet caretSet,
        IReadOnlyList<string> replacementLines)
    {
        var edit = TextCaretSetOperations.CreatePaste(
            Snapshot,
            caretSet,
            replacementLines,
            TextBlockSelectionOperations.GetPreferredLineEnding(
                Snapshot,
                _preferredLineEnding),
            TabDisplaySize);
        _document.Replace(edit.Range, edit.Replacement, edit.CaretSet);
    }

    private bool TryCreateBlockSelectionCaretSet(
        TextBlockSelection selection,
        out TextCaretSet caretSet)
    {
        if (selection.CoordinateSpace == TextBlockSelectionCoordinateSpace.VisualRows)
        {
            return _defaultRenderer.TextRenderer.TryCreateVisualBlockSelectionCaretSet(
                selection,
                TabDisplaySize,
                out caretSet);
        }

        caretSet = TextCaretSetOperations.FromBlockSelection(
            Snapshot,
            selection,
            TabDisplaySize);
        return true;
    }

    private string GetBlockSelectionText(TextBlockSelection selection)
    {
        if (selection.CoordinateSpace == TextBlockSelectionCoordinateSpace.VisualRows)
        {
            return TryCreateBlockSelectionCaretSet(selection, out var visualCarets)
                ? GetCaretSetSelectedText(visualCarets)
                : string.Empty;
        }

        return TextBlockSelectionOperations.GetSelectedText(
            Snapshot,
            selection,
            TabDisplaySize,
            TextBlockSelectionOperations.GetPreferredLineEnding(
                Snapshot,
                _preferredLineEnding));
    }

    private static void SetClipboardText(string text)
    {
        var dataPackage = new DataPackage();
        dataPackage.SetText(text);
        Clipboard.SetContent(dataPackage);
    }

    private async Task PasteFromClipboardAsync()
    {
        if (DateTimeOffset.UtcNow < _clipboardUnavailableUntil)
        {
            return;
        }

        string text;
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
            {
                return;
            }

            text = await content.GetTextAsync();
            _clipboardUnavailableUntil = default;
        }
        catch (COMException exception) when (
            unchecked((uint)exception.HResult) == 0x800401D3u)
        {
            // The clipboard broker can transiently expose malformed data while
            // another application is replacing the clipboard. Repeating the
            // WinRT request for every queued Ctrl+V only amplifies that race.
            _clipboardUnavailableUntil = DateTimeOffset.UtcNow.AddMilliseconds(250);
            LogDiagnostic(
                AzunyanDiagnosticCategory.Clipboard,
                $"clipboard-text-unavailable hresult=0x{exception.HResult:X8}; "
                + DescribeDiagnosticState());
            return;
        }

        if (_disposed)
        {
            return;
        }

        if (_blockSelection is { } selection)
        {
            var lines = SplitBlockInput(text);
            ApplyDocumentCommand(() => ApplyBlockReplacement(
                selection,
                lines,
                repeatSingleLine: lines.Length == 1));
            return;
        }

        if (Document.CaretSet.Count > 1)
        {
            var lines = SplitBlockInput(text);
            ApplyDocumentCommand(() =>
            {
                var edit = TextCaretSetOperations.CreatePaste(
                    Snapshot,
                    Document.CaretSet,
                    lines,
                    TextBlockSelectionOperations.GetPreferredLineEnding(
                        Snapshot,
                        _preferredLineEnding),
                    TabDisplaySize);
                _document.Replace(edit.Range, edit.Replacement, edit.CaretSet);
            });
            return;
        }

        ApplyDocumentCommand(() => _document.Replace(Document.Selection.Range, text));
    }

    private static string[] SplitBlockInput(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None);
    }

    private string GetCaretSetSelectedText()
        => GetCaretSetSelectedText(Document.CaretSet);

    private string GetCaretSetSelectedText(TextCaretSet caretSet)
    {
        var lineEnding = TextBlockSelectionOperations.GetPreferredLineEnding(
            Snapshot,
            _preferredLineEnding);
        return string.Join(
            lineEnding,
            caretSet.Select(caret => Snapshot.GetText(caret.Selection.Range)));
    }

    private void ApplyCaretSetReplacement(string text)
    {
        var replacements = Enumerable
            .Repeat<string?>(text, Document.CaretSet.Count)
            .ToArray();
        var edit = TextCaretSetOperations.CreateReplacement(
            Snapshot,
            Document.CaretSet,
            replacements,
            TabDisplaySize);
        _document.Replace(edit.Range, edit.Replacement, edit.CaretSet);
    }

    private void ApplyPrimaryCaretSetReplacement(
        TextChange change,
        TextSelection primarySelection)
    {
        var replacements = new string?[Document.CaretSet.Count];
        replacements[Document.CaretSet.PrimaryIndex] = change.NewText;
        var edit = TextCaretSetOperations.CreateReplacement(
            Snapshot,
            Document.CaretSet,
            replacements,
            TabDisplaySize);
        var states = edit.CaretSet.ToList();
        var primaryIndex = edit.CaretSet.PrimaryIndex;
        states[primaryIndex] = states[primaryIndex].WithSelection(primarySelection);
        _document.Replace(
            edit.Range,
            edit.Replacement,
            new TextCaretSet(states, primaryIndex));
    }

    private void ApplyCaretSetDeletion(bool backward = false)
    {
        var edit = TextCaretSetOperations.CreateDeletion(
            Snapshot,
            Document.CaretSet,
            backward,
            TabDisplaySize);
        _document.Replace(edit.Range, edit.Replacement, edit.CaretSet);
    }

    private void ApplyCaretSetDeletion(bool backward, bool byWord)
    {
        var edit = TextCaretSetOperations.CreateDeletion(
            Snapshot,
            Document.CaretSet,
            backward,
            TabDisplaySize,
            byWord);
        _document.Replace(edit.Range, edit.Replacement, edit.CaretSet);
    }

    private void ApplyManagedNewLine()
    {
        var caretSet = Document.CaretSet;
        if (_blockSelection is { } blockSelection)
        {
            if (!TryCreateBlockSelectionCaretSet(blockSelection, out caretSet))
            {
                _blockSelection = null;
                return;
            }

            _blockSelection = null;
        }

        if (!_autoIndentOnEnter)
        {
            var lineEnding = TextBlockSelectionOperations.GetPreferredLineEnding(
                Snapshot,
                _preferredLineEnding);
            var edit = TextCaretSetOperations.CreateReplacement(
                Snapshot,
                caretSet,
                Enumerable.Repeat<string?>(lineEnding, caretSet.Count).ToArray(),
                TabDisplaySize);
            _document.Replace(edit.Range, edit.Replacement, edit.CaretSet);
            return;
        }

        var newLineEdit = TextCaretSetOperations.CreateNewLineWithAutoIndent(
            Snapshot,
            caretSet,
            _preferredLineEnding,
            TabDisplaySize);
        _document.Replace(
            newLineEdit.Range,
            newLineEdit.Replacement,
            newLineEdit.CaretSet);
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
            LogDiagnostic(
                AzunyanDiagnosticCategory.Input,
                $"native-composition-changed-stale generation={args.Generation}; currentGeneration={_inputWindowGeneration}");
            return;
        }

        LogDiagnostic(
            AzunyanDiagnosticCategory.Input,
            $"native-composition-changed generation={args.Generation}; composing={args.IsComposing}; "
            + $"range={args.CompositionRange?.ToString() ?? "none"}; {DescribeDiagnosticState()}");
        _compositionRange = args.CompositionRange;
        _pendingProviderDocumentChange = null;
        _completionRequested = false;
        _explicitCompletionRequested = false;
        HideCompletionPopup();
        RenderViewport();
        CompositionChanged?.Invoke(this, EventArgs.Empty);
        if (!args.IsComposing)
        {
            DrainPendingCompositionOperations();
            RequestPendingPasteBatchRefresh();
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
            LogDiagnostic(
                AzunyanDiagnosticCategory.Input,
                $"native-selection-changed generation={args.Generation}; selection={args.Selection}; "
                + DescribeDiagnosticState());
            // Managed paste updates the document first and intentionally
            // delays the native sliding-window refresh. SelectionChanged can
            // therefore arrive from the previous native snapshot. Applying
            // it here would move the document caret backwards.
            if (_pasteBatchActive
                || _pasteBatchRefreshPending
                || _pasteBatchRefreshScheduled)
            {
                return;
            }

            if (_selectionPointerId is not null)
            {
                return;
            }

            // Outside an active composition the projected document owns
            // selection. Committed text carries its authoritative selection
            // in InputChanged; accepting an independent TextBox selection
            // here would reintroduce native Home/End/arrow movement from the
            // bounded IME window.
            if (!IsComposing)
            {
                return;
            }

            // The native textbox moves its primary selection as part of text
            // input before TextChanged is raised.  That intermediate event
            // must not collapse the virtual caret set; the projected editor
            // owns movement while multi-caret mode is active.
            if (Document.CaretSet.Count > 1)
            {
                return;
            }

            var hadBlockSelection = _blockSelection is not null;
            _blockSelection = null;
            _document.Selection = args.Selection;
            if (hadBlockSelection)
            {
                RenderViewport();
            }
        }
    }

    private void OnDocumentSelectionChanged(object? sender, EventArgs args)
    {
        if (_applyingDocumentCommand || _applyingInputChange)
        {
            _automationPeer?.NotifySelectionChanged();
            return;
        }

        if (_selectionPointerId is not null)
        {
            _automationPeer?.NotifySelectionChanged();
            return;
        }

        SyncInputWindow();
        RenderViewport();
        _automationPeer?.NotifySelectionChanged();
        if (!IsComposing)
        {
            RequestProviderResults(false, false, true, requestCompletion: _completionRequested);
        }
    }

    private void OnDocumentCaretSetChanged(object? sender, EventArgs args)
    {
        if (_applyingDocumentCommand || _applyingInputChange)
        {
            _automationPeer?.NotifySelectionChanged();
            return;
        }

        if (_selectionPointerId is not null)
        {
            _automationPeer?.NotifySelectionChanged();
            return;
        }

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
        var hadBlockSelection = _blockSelection is not null;
        _blockSelection = null;
        var oldText = Snapshot.Text;
        _document.Replace(range, replacement);
        SyncInputWindow();
        if (string.Equals(oldText, Snapshot.Text, StringComparison.Ordinal))
        {
            if (hadBlockSelection)
            {
                RenderViewport();
            }

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

        if (_nativeKeysDown.Count != 0)
        {
            // Keep the projected document and caret responsive while a
            // shortcut modifier remains held, but do not mutate the native
            // TextBox from inside its active key pipeline. KeyUp requests the
            // deferred synchronization after the complete chord is released.
            _inputWindowSynchronizationPending = true;
            return;
        }

        var currentWindow = InputWindow.WindowRange;
        var window = CalculateInputWindow(currentWindow);
        var text = Snapshot.GetText(window);
        TextRange? compositionRange = _compositionRange is { } compositionInWindow
            && window.Contains(compositionInWindow)
                ? compositionInWindow
                : null;
        if (window == currentWindow
            && string.Equals(InputWindow.WindowText, text, StringComparison.Ordinal)
            && InputWindow.Selection == Document.Selection
            && InputWindow.CompositionRange == compositionRange)
        {
            _inputWindowSynchronizationPending = false;
            return;
        }

        var generation = checked(++_inputWindowGeneration);

        LogDiagnosticStage(
            AzunyanDiagnosticCategory.Input,
            "before-native-window-set",
            $"generation={generation}; windowStart={window.Start}; windowLength={window.Length}; "
            + $"selection={Document.Selection}");

        _synchronizingInputWindow = true;
        try
        {
            InputWindow.SetWindow(
                generation,
                window.Start,
                text,
                Document.Selection,
                compositionRange);
        }
        finally
        {
            _synchronizingInputWindow = false;
        }

        _inputWindowSynchronizationPending = false;
        LogDiagnosticStage(
            AzunyanDiagnosticCategory.Input,
            "after-native-window-set",
            $"generation={generation}; windowStart={window.Start}; windowLength={window.Length}");
    }

    private TextRange CalculateInputWindow(TextRange currentWindow) =>
        IsComposing
            && currentWindow.Contains(Document.Selection.Range)
            && _compositionRange is { } composition
            && currentWindow.Contains(composition)
                ? currentWindow
                : _inputWindowCalculator.Calculate(
                    Snapshot,
                    Document.Selection,
                    _compositionRange,
                    currentWindow);

    private bool IsInputWindowSynchronized()
    {
        var currentWindow = InputWindow.WindowRange;
        var window = CalculateInputWindow(currentWindow);
        TextRange? compositionRange = _compositionRange is { } compositionInWindow
            && window.Contains(compositionInWindow)
                ? compositionInWindow
                : null;
        return window == currentWindow
            && string.Equals(
                InputWindow.WindowText,
                Snapshot.GetText(window),
                StringComparison.Ordinal)
            && InputWindow.Selection == Document.Selection
            && InputWindow.CompositionRange == compositionRange;
    }

    private void RequestInputWindowSynchronization()
    {
        if (!_inputWindowSynchronizationPending
            || _inputWindowSynchronizationScheduled
            || IsComposing)
        {
            return;
        }

        _inputWindowSynchronizationScheduled = true;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                _inputWindowSynchronizationScheduled = false;
                if (!_disposed && !IsComposing)
                {
                    SyncInputWindow();
                }
            }))
        {
            _inputWindowSynchronizationScheduled = false;
            throw new InvalidOperationException(
                "The editor dispatcher is no longer available.");
        }
    }

    private void RequestPendingPasteBatchRefresh()
    {
        if (!_pasteBatchRefreshPending
            || _pasteOperationRunning
            || IsComposing
            || _nativeKeysDown.Count != 0)
        {
            return;
        }

        _pasteBatchRefreshTimer ??= DispatcherQueue.CreateTimer();
        _pasteBatchRefreshTimer.Interval = TimeSpan.FromMilliseconds(120);
        _pasteBatchRefreshTimer.IsRepeating = false;
        _pasteBatchRefreshTimer.Stop();
        _pasteBatchRefreshScheduled = true;
        _pasteBatchRefreshTimer.Tick -= OnPasteBatchRefreshTimerTick;
        _pasteBatchRefreshTimer.Tick += OnPasteBatchRefreshTimerTick;
        _pasteBatchRefreshTimer.Start();
    }

    private void OnPasteBatchRefreshTimerTick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        _pasteBatchRefreshScheduled = false;
        if (_disposed
            || _pasteOperationRunning
            || IsComposing
            || _nativeKeysDown.Count != 0
            || !_pasteBatchRefreshPending)
        {
            return;
        }

        try
        {
            SyncInputWindow();
            RenderViewport();
            _pasteBatchRefreshPending = false;
            LogDiagnostic(
                AzunyanDiagnosticCategory.Clipboard,
                $"paste-batch-refresh; {DescribeDiagnosticState()}");
        }
        catch (Exception exception)
        {
            // A dispatcher callback must not leak a managed exception into
            // the WinUI/CoreMessaging input loop. Keep the pending flag set
            // so a later input turn can retry.
            ReportDiagnosticException("PasteBatchRefresh", exception);
            LogDiagnostic(
                AzunyanDiagnosticCategory.Clipboard,
                $"paste-batch-refresh-failed; {exception}");
        }
    }

    private void RunAfterComposition(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsComposing)
        {
            _pendingCompositionOperations.Enqueue(action);
            return;
        }

        action();
    }

    private void DrainPendingCompositionOperations()
    {
        if (IsComposing)
        {
            return;
        }

        while (_pendingCompositionOperations.Count > 0)
        {
            _pendingCompositionOperations.Dequeue().Invoke();
        }
    }

    private bool TryGetRendererCaretRect(
        DocumentAnchor anchor,
        out Rect rect)
    {
        if (_renderer is null)
        {
            rect = default;
            return false;
        }

        return _renderer.TryGetCaretRect(anchor, out rect);
    }

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs args)
    {
        var isRepeat = !_nativeKeysDown.Add(args.Key);
        LogDiagnostic(
            AzunyanDiagnosticCategory.Key,
            $"keydown key={args.Key}; repeat={isRepeat}; {DescribeDiagnosticState()}");

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
                    QueueKeyEdit(args.Key, () => TryAcceptSelectedCompletion(), repeatable: false);
                    args.Handled = true;
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
        if (args.Key == VirtualKey.V && control)
        {
            var pasteMode = _blockSelection is not null
                ? "block-selection"
                : Document.CaretSet.Count > 1
                    ? "caret-set"
                    : "single-caret";
            LogDiagnostic(
                AzunyanDiagnosticCategory.Key,
                $"ctrl-v-keydown mode={pasteMode}; implementation=managed; "
                + DescribeDiagnosticState());
        }

        switch (args.Key)
        {
            case OemOpenBracketKey when control && !menu && !IsComposing:
                QueueKeyEdit(args.Key, () =>
                {
                    ApplyDocumentCommand(() =>
                    {
                        if (_blockSelection is not null)
                        {
                            _blockSelection = null;
                        }
                        else
                        {
                            _document.SetCaretSet(
                                TextCaretSetOperations.MoveToMatchingBracket(
                                    Snapshot,
                                    Document.CaretSet,
                                    extendSelection,
                                    TabDisplaySize));
                        }
                    });
                    ScrollSelectionIntoView();
                }, repeatable: false);
                args.Handled = true;
                break;
            case VirtualKey.A when control && !menu && !IsComposing:
                QueueKeyEdit(args.Key, () =>
                {
                    ApplyDocumentCommand(() =>
                    {
                        _blockSelection = null;
                        _document.Selection = new TextSelection(0, Snapshot.Length);
                    });
                }, repeatable: false);
                args.Handled = true;
                break;
            case VirtualKey.Enter
                when !IsComposing
                && !control
                && !menu
                && AcceptsReturn:
                QueueKeyEdit(args.Key, () =>
                {
                    ApplyDocumentCommand(ApplyManagedNewLine);
                    ScrollSelectionIntoView();
                }, repeatable: true);
                args.Handled = true;
                break;
            case VirtualKey.Tab
                when !IsComposing
                && !control
                && !menu:
                QueueKeyEdit(args.Key, () =>
                {
                    ApplyDocumentCommand(() =>
                    {
                        if (!IsComposing)
                        {
                            TextEditorCommands.IndentSelection(
                                Document,
                                extendSelection,
                                _indentSize,
                                _indentationInputMode);
                        }
                    });
                }, repeatable: true);
                args.Handled = true;
                break;
            case VirtualKey.Z when control && !menu && !IsComposing:
                if (extendSelection)
                {
                    QueueKeyEdit(args.Key, () => _ = RedoDocument(), repeatable: false);
                }
                else
                {
                    QueueKeyEdit(args.Key, () => _ = UndoDocument(), repeatable: false);
                }

                args.Handled = true;
                break;
            case VirtualKey.Y when control && !menu && !IsComposing:
                QueueKeyEdit(args.Key, () => _ = RedoDocument(), repeatable: false);
                args.Handled = true;
                break;
            case VirtualKey.C when control && !menu
                && !IsComposing:
                QueueKeyEdit(args.Key, CopySelectionToClipboard, repeatable: false);
                args.Handled = true;
                break;
            case VirtualKey.X when control && !menu
                && !IsComposing:
                QueueKeyEdit(args.Key, CutSelectionToClipboard, repeatable: false);
                args.Handled = true;
                break;
            case VirtualKey.V when control && !menu && !IsComposing:
                QueueKeyEdit(args.Key, PasteFromClipboard, repeatable: false);
                args.Handled = true;
                break;
            case VirtualKey.Left when !IsComposing && !menu:
                QueueKeyEdit(args.Key, () =>
                {
                    ApplyDocumentCommand(() =>
                    {
                        if (_blockSelection is not null)
                        {
                            _blockSelection = null;
                        }
                        else
                        {
                            _document.SetCaretSet(TextCaretSetOperations.MoveHorizontal(
                                Snapshot,
                                Document.CaretSet,
                                -1,
                                extendSelection,
                                byWord: control,
                                tabDisplaySize: TabDisplaySize));
                        }
                    });
                }, repeatable: true);
                args.Handled = true;
                break;
            case VirtualKey.Right when !IsComposing && !menu:
                QueueKeyEdit(args.Key, () =>
                {
                    ApplyDocumentCommand(() =>
                    {
                        if (_blockSelection is not null)
                        {
                            _blockSelection = null;
                        }
                        else
                        {
                            _document.SetCaretSet(TextCaretSetOperations.MoveHorizontal(
                                Snapshot,
                                Document.CaretSet,
                                1,
                                extendSelection,
                                byWord: control,
                                tabDisplaySize: TabDisplaySize));
                        }
                    });
                }, repeatable: true);
                args.Handled = true;
                break;
            case VirtualKey.Up
                when !IsComposing
                && control
                && !menu
                && !extendSelection
                && IsProjectedTextSurface:
                QueueKeyEdit(
                    args.Key,
                    () => ScrollProjectedBy(-_lineHeight),
                    repeatable: true);
                args.Handled = true;
                break;
            case VirtualKey.Down
                when !IsComposing
                && control
                && !menu
                && !extendSelection
                && IsProjectedTextSurface:
                QueueKeyEdit(
                    args.Key,
                    () => ScrollProjectedBy(_lineHeight),
                    repeatable: true);
                args.Handled = true;
                break;
            case VirtualKey.Up when !IsComposing && !control && !menu:
                QueueKeyEdit(args.Key, () =>
                {
                    ApplyProjectedNavigation(
                        AzunyanEditorNavigationKind.Up,
                        extendSelection,
                        () => TextCaretSetOperations.MoveVertical(
                            Snapshot,
                            Document.CaretSet,
                            -1,
                            TabDisplaySize,
                            extendSelection));
                    ScrollSelectionIntoView();
                }, repeatable: true);
                args.Handled = true;
                break;
            case VirtualKey.Down when !IsComposing && !control && !menu:
                QueueKeyEdit(args.Key, () =>
                {
                    ApplyProjectedNavigation(
                        AzunyanEditorNavigationKind.Down,
                        extendSelection,
                        () => TextCaretSetOperations.MoveVertical(
                            Snapshot,
                            Document.CaretSet,
                            1,
                            TabDisplaySize,
                            extendSelection));
                    ScrollSelectionIntoView();
                }, repeatable: true);
                args.Handled = true;
                break;
            case VirtualKey.PageUp when !IsComposing && !control && !menu:
            case VirtualKey.PageDown when !IsComposing && !control && !menu:
                var pageDirection = args.Key == VirtualKey.PageUp ? -1 : 1;
                QueueKeyEdit(args.Key, () =>
                {
                    var pageLines = Math.Max(
                        1,
                        (int)Math.Floor(Math.Max(1, EditorHost.ActualHeight) / _lineHeight) - 1);
                    var direction = pageDirection * pageLines;
                    ApplyProjectedNavigation(
                        pageDirection < 0
                            ? AzunyanEditorNavigationKind.PageUp
                            : AzunyanEditorNavigationKind.PageDown,
                        extendSelection,
                        () => TextCaretSetOperations.MoveVertical(
                            Snapshot,
                            Document.CaretSet,
                            direction,
                            TabDisplaySize,
                            extendSelection));
                    ScrollSelectionIntoView();
                }, repeatable: true);
                args.Handled = true;
                break;
            case VirtualKey.Home when !IsComposing && !menu:
                QueueKeyEdit(args.Key, () =>
                {
                    if (control)
                    {
                        ApplyDocumentCommand(() =>
                            _document.SetCaretSet(TextCaretSetOperations.MoveToLineBoundary(
                                Snapshot,
                                Document.CaretSet,
                                end: false,
                                documentBoundary: true,
                                extendSelection,
                                TabDisplaySize)));
                    }
                    else
                    {
                        ApplyProjectedNavigation(
                            AzunyanEditorNavigationKind.SmartHome,
                            extendSelection,
                            () => TextCaretSetOperations.MoveToSmartLineStart(
                                Snapshot,
                                Document.CaretSet,
                                extendSelection,
                                TabDisplaySize));
                    }
                    ScrollSelectionIntoView();
                }, repeatable: true);
                args.Handled = true;
                break;
            case VirtualKey.End when !IsComposing && !menu:
                QueueKeyEdit(args.Key, () =>
                {
                    if (control)
                    {
                        ApplyDocumentCommand(() =>
                            _document.SetCaretSet(TextCaretSetOperations.MoveToLineBoundary(
                                Snapshot,
                                Document.CaretSet,
                                end: true,
                                documentBoundary: true,
                                extendSelection,
                                TabDisplaySize)));
                    }
                    else
                    {
                        ApplyProjectedNavigation(
                            AzunyanEditorNavigationKind.End,
                            extendSelection,
                            () => TextCaretSetOperations.MoveToLineBoundary(
                                Snapshot,
                                Document.CaretSet,
                                end: true,
                                documentBoundary: false,
                                extendSelection,
                                TabDisplaySize));
                    }
                    ScrollSelectionIntoView();
                }, repeatable: true);
                args.Handled = true;
                break;
            case VirtualKey.Back when !IsComposing && !menu:
                QueueKeyEdit(args.Key, () =>
                {
                    ApplyDocumentCommand(() =>
                    {
                        if (_blockSelection is { } blockSelection)
                        {
                            ApplyBlockReplacement(
                                blockSelection,
                                new[] { string.Empty },
                                repeatSingleLine: true);
                        }
                        else
                        {
                            ApplyCaretSetDeletion(backward: true, byWord: control);
                        }
                    });
                }, repeatable: true);
                args.Handled = true;
                break;
            case VirtualKey.Delete when !IsComposing && !menu:
                QueueKeyEdit(args.Key, () =>
                {
                    ApplyDocumentCommand(() =>
                    {
                        if (_blockSelection is { } blockSelection)
                        {
                            ApplyBlockReplacement(
                                blockSelection,
                                new[] { string.Empty },
                                repeatSingleLine: true);
                        }
                        else
                        {
                            ApplyCaretSetDeletion(backward: false, byWord: control);
                        }
                    });
                }, repeatable: true);
                args.Handled = true;
                break;
        }

    }

    private void QueueKeyEdit(VirtualKey key, Action action, bool repeatable)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!repeatable && !_queuedNonRepeatingKeys.Add(key))
        {
            LogDiagnostic(
                AzunyanDiagnosticCategory.Key,
                $"ignored-repeated-key-edit key={key}; {DescribeDiagnosticState()}");
            return;
        }

        var inputEpoch = _keyInputEpoch;
        LogDiagnostic(
            AzunyanDiagnosticCategory.Key,
            $"queued-key-edit key={key}; {DescribeDiagnosticState()}");
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                if (!_disposed && inputEpoch == _keyInputEpoch)
                {
                    ExecutePendingKeyEdit(key, action);
                }
            }))
        {
            throw new InvalidOperationException(
                "The editor dispatcher is no longer available.");
        }
    }

    private void ExecutePendingKeyEdit(VirtualKey key, Action action)
    {
        var operation = $"key-edit-{Interlocked.Increment(ref _diagnosticOperationSequence)}:{key}";
        _diagnosticOperation = operation;
        LogDiagnostic(
            AzunyanDiagnosticCategory.Key,
            $"BEGIN {operation}; {DescribeDiagnosticState()}");
        try
        {
            action();
            LogDiagnostic(
                AzunyanDiagnosticCategory.Key,
                $"END {operation}; {DescribeDiagnosticState()}");
        }
        catch (Exception exception)
        {
            ReportDiagnosticException("KeyEdit", exception);
            LogDiagnostic(
                AzunyanDiagnosticCategory.Key,
                $"FAILED {operation}; {exception}");
            throw;
        }
        finally
        {
            _diagnosticOperation = null;
        }
    }

    private void LogDiagnosticStage(
        AzunyanDiagnosticCategory category,
        string stage,
        string details)
    {
        var operation = _diagnosticOperation ?? _diagnosticPasteOperation;
        var operationPrefix = operation is null ? string.Empty : $"{operation} ";
        LogDiagnostic(category, $"{operationPrefix}{stage}; {details}");
    }

    private void LogDiagnostic(
        AzunyanDiagnosticCategory category,
        string message)
    {
        if ((DiagnosticCategories & category) == 0)
        {
            return;
        }

        try
        {
            DiagnosticSink?.Invoke(category, message);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Azunyan editor diagnostic sink failed: {exception}");
        }
    }

    private void OnNativeInputCallbackException(string source, Exception exception)
    {
        ReportDiagnosticException($"NativeInput/{source}", exception);
        LogDiagnostic(
            AzunyanDiagnosticCategory.Input,
            $"native-input-callback-failed source={source}; {exception}");
    }

    private void ReportDiagnosticException(string source, Exception exception)
    {
        try
        {
            DiagnosticExceptionSink?.Invoke(source, exception);
        }
        catch (Exception sinkException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Azunyan editor diagnostic exception sink failed: {sinkException}");
        }
    }

    private string DescribeDiagnosticState()
    {
        var selection = Document.Selection;
        var blockSelection = _blockSelection is { } block
            ? block.CoordinateSpace.ToString()
            : "none";
        return $"snapshotLength={Snapshot.Length}; selection={selection}; "
            + $"caretCount={Document.CaretSet.Count}; wrapping={TextWrapping}; "
            + $"blockSelection={blockSelection}; inputWindowStart={InputWindow.WindowStart}; "
            + $"inputWindowLength={InputWindow.WindowText.Length}; composing={IsComposing}; "
            + $"nativeKeys={string.Join(',', _nativeKeysDown)}; "
            + $"pasteBatch={_pasteBatchActive}; "
            + $"pasteRefreshPending={_pasteBatchRefreshPending}; "
            + $"pasteRefreshScheduled={_pasteBatchRefreshScheduled}";
    }

    private void OnInputKeyUp(object sender, KeyRoutedEventArgs args)
    {
        _nativeKeysDown.Remove(args.Key);
        _queuedNonRepeatingKeys.Remove(args.Key);
        LogDiagnostic(
            AzunyanDiagnosticCategory.Key,
            $"keyup key={args.Key}; {DescribeDiagnosticState()}");
        if (_nativeKeysDown.Count == 0)
        {
            if (_pasteBatchRefreshPending)
            {
                RequestPendingPasteBatchRefresh();
            }
            else
            {
                RequestInputWindowSynchronization();
            }
        }
    }

    private void ApplyProjectedNavigation(
        AzunyanEditorNavigationKind kind,
        bool extendSelection,
        Func<TextCaretSet> fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        ApplyDocumentCommand(() =>
        {
            var next = _renderer is IAzunyanEditorNavigationGeometry navigation
                && navigation.TryNavigate(
                    new AzunyanEditorNavigationRequest(
                        Document.CaretSet,
                        kind,
                        extendSelection,
                        TabDisplaySize),
                    out var projected)
                ? projected
                : fallback();
            _blockSelection = null;
            _document.SetCaretSet(next);
        });
    }

    private void ScrollProjectedBy(double delta)
    {
        if (!IsProjectedTextSurface || !double.IsFinite(delta))
        {
            return;
        }

        var offset = Math.Clamp(
            _projectedVerticalOffset + delta,
            0,
            ProjectedVerticalScrollBar.Maximum);
        _synchronizingProjectedScroll = true;
        try
        {
            _projectedVerticalOffset = offset;
            ProjectedVerticalScrollBar.Value = offset;
        }
        finally
        {
            _synchronizingProjectedScroll = false;
        }

        RenderViewport();
        RequestProviderResults(false, true, false);
    }

    private static bool TryGetLeadingLineEndingLength(string text, out int length)
    {
        if (text.StartsWith("\r\n", StringComparison.Ordinal))
        {
            length = 2;
            return true;
        }

        if (text.StartsWith('\n') || text.StartsWith('\r'))
        {
            length = 1;
            return true;
        }

        length = 0;
        return false;
    }

    private void ApplyDocumentCommand(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var previousSnapshot = Snapshot;
        _applyingDocumentCommand = true;
        try
        {
            action();
        }
        finally
        {
            _applyingDocumentCommand = false;
        }

        if (_pasteBatchActive)
        {
            // Consecutive managed pastes can arrive faster than the native
            // TextBox text service can settle. Keep the document authoritative
            // and coalesce the native sliding-window update until the batch is
            // idle.
            _inputWindowSynchronizationPending = true;
            _pasteBatchRefreshPending = true;
        }
        else
        {
            SyncInputWindow();
            RenderViewport();
        }
        if (ReferenceEquals(previousSnapshot, Snapshot))
        {
            // SelectionChanged is intentionally suppressed while the command
            // mutates the document model. Keep position-scoped results and
            // the provider frame selection in sync after the command ends.
            RequestProviderResults(
                false,
                false,
                true,
                requestCompletion: _completionRequested);
            return;
        }

        var documentChange = _pendingProviderDocumentChange;
        _pendingProviderDocumentChange = null;
        RequestProviderResults(
            true,
            true,
            true,
            requestCompletion: _completionRequested,
            documentChange: documentChange);
    }

    private void OnInputFocusChanged(object? sender, EventArgs args)
    {
        LogDiagnostic(
            AzunyanDiagnosticCategory.Input,
            $"native-focus-changed state={InputWindow.FocusState}; {DescribeDiagnosticState()}");
        if (InputWindow.FocusState == FocusState.Unfocused)
        {
            // A window deactivation can lose the physical KeyUp messages.
            // Do not retain delayed commands or native suppression markers
            // across the next focus session.
            _nativeKeysDown.Clear();
            _queuedNonRepeatingKeys.Clear();
            _keyInputEpoch++;
            InputWindow.NativeTextBoxControl.ResetHandledKeyState();
        }

        _automationPeer?.NotifyFocusChanged();
    }

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

    private static VirtualKeyModifiers GetPointerModifiers(
        PointerRoutedEventArgs args)
    {
        var modifiers = args.KeyModifiers;
        if (IsKeyDown(VirtualKey.Control))
        {
            modifiers |= VirtualKeyModifiers.Control;
        }

        if (IsKeyDown(VirtualKey.Menu))
        {
            modifiers |= VirtualKeyModifiers.Menu;
        }

        if (IsKeyDown(VirtualKey.Shift))
        {
            modifiers |= VirtualKeyModifiers.Shift;
        }

        return modifiers;
    }

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

        StopPointerSelection();

        if (!_defaultRenderer.TextRenderer.TryHitTest(
                point.Position.X,
                point.Position.Y,
                InputWindow.NativeTextBoxControl.Padding.Left,
                InputWindow.NativeTextBoxControl.Padding.Top,
                GetHorizontalOffset(),
                GetVerticalOffset(),
                _characterWidth,
                out var anchor,
                out var foldId,
                out var adornmentId,
                out var blockPosition))
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
            var pointerModifiers = GetPointerModifiers(args);
            var isShiftSelection = pointerModifiers.HasFlag(VirtualKeyModifiers.Shift);
            var isBlockSelection = pointerModifiers.HasFlag(VirtualKeyModifiers.Menu);
            var pointerAnchor = isShiftSelection && !isBlockSelection
                ? Document.Selection.Anchor
                : anchor.Position.Offset;
            if (isBlockSelection)
            {
                _pointerBlockSelectionAnchor = blockPosition;
                _pointerSelectingBlock = true;
                _blockSelection = new TextBlockSelection(
                    blockPosition,
                    blockPosition,
                    GetBlockSelectionCoordinateSpace());
            }
            else
            {
                _blockSelection = null;
                _pointerSelectionAnchor = pointerAnchor;
            }

            _selectionPointerId = point.PointerId;
            EditorPointerSurface.CapturePointer(args.Pointer);
            SetProjectedDocumentSelection(
                isShiftSelection && !isBlockSelection
                    ? new TextSelection(pointerAnchor, anchor.Position.Offset)
                    : TextSelection.Caret(anchor.Position.Offset),
                anchor);
            SyncInputWindow();
            RenderViewport();
        }

        InputWindow.Focus(FocusState.Pointer);
        args.Handled = true;
    }

    private void OnInputPointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (_selectionPointerId is not uint selectionPointerId)
        {
            return;
        }

        var point = args.GetCurrentPoint(EditorPointerSurface);
        if (point.PointerId != selectionPointerId)
        {
            return;
        }

        CompletePointerSelection();
        args.Handled = true;
    }

    private void OnInputPointerCanceled(object sender, PointerRoutedEventArgs args) =>
        CompletePointerSelection();

    private void OnInputPointerCaptureLost(object sender, PointerRoutedEventArgs args) =>
        CompletePointerSelection();

    private void CompletePointerSelection()
    {
        if (_selectionPointerId is null)
        {
            return;
        }

        var blockSelection = _pointerSelectingBlock ? _blockSelection : null;
        StopPointerSelection();
        if (blockSelection is { } committedSelection)
        {
            CommitBlockSelection(committedSelection);
        }
        else
        {
            SyncInputWindow();
            RenderViewport();
        }
    }

    private void RequestPointerRender()
    {
        if (_disposed || _pointerRenderScheduled)
        {
            return;
        }

        _pointerRenderScheduled = true;
        if (DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () =>
                {
                    _pointerRenderScheduled = false;
                    if (!_disposed && _selectionPointerId is not null)
                    {
                        RenderViewport();
                    }
                }))
        {
            return;
        }

        _pointerRenderScheduled = false;
        RenderViewport();
    }

    private void StopPointerSelection()
    {
        _pointerSelectionAnchor = null;
        _pointerBlockSelectionAnchor = null;
        _pointerSelectingBlock = false;
        _selectionPointerId = null;
        EditorPointerSurface.ReleasePointerCaptures();
    }

    private void UpdateBlockSelection(
        TextBlockSelection selection,
        int activeOffset)
    {
        var changed = _blockSelection != selection;
        _blockSelection = selection;
        var caret = TextSelection.Caret(activeOffset);
        if (_document.Selection != caret)
        {
            _document.Selection = caret;
        }

        if (changed)
        {
            RequestPointerRender();
        }
    }

    private TextBlockSelectionCoordinateSpace GetBlockSelectionCoordinateSpace() =>
        TextWrapping == TextWrapping.Wrap
            ? TextBlockSelectionCoordinateSpace.VisualRows
            : TextBlockSelectionCoordinateSpace.LogicalLines;

    private void CommitBlockSelection(TextBlockSelection selection)
    {
        if (!TryCreateBlockSelectionCaretSet(selection, out var caretSet))
        {
            _blockSelection = null;
            SyncInputWindow();
            RenderViewport();
            return;
        }

        _blockSelection = null;
        if (caretSet.Count == 1)
        {
            _document.Selection = caretSet.Primary.Selection;
        }
        else
        {
            _document.SetCaretSet(caretSet);
        }

        SyncInputWindow();
        RenderViewport();
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
        if (_selectionPointerId is uint selectionPointerId
            && point.PointerId == selectionPointerId
            && _pointerSelectingBlock
            && _pointerBlockSelectionAnchor is { } blockSelectionAnchor)
        {
            if (point.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse
                && !point.Properties.IsLeftButtonPressed)
            {
                var completedSelection = _blockSelection;
                StopPointerSelection();
                if (completedSelection is { })
                {
                    CommitBlockSelection(completedSelection.Value);
                }
                return;
            }

            if (_defaultRenderer.TextRenderer.TryHitTest(
                    point.Position.X,
                    point.Position.Y,
                    InputWindow.NativeTextBoxControl.Padding.Left,
                    InputWindow.NativeTextBoxControl.Padding.Top,
                    GetHorizontalOffset(),
                    GetVerticalOffset(),
                    _characterWidth,
                    out var dragAnchor,
                    out _,
                    out _,
                    out var dragBlockPosition))
            {
                UpdateBlockSelection(
                    new TextBlockSelection(
                        blockSelectionAnchor,
                        dragBlockPosition,
                        GetBlockSelectionCoordinateSpace()),
                    dragAnchor.Position.Offset);
            }

            args.Handled = true;
            return;
        }

        if (_selectionPointerId is uint normalSelectionPointerId
            && point.PointerId == normalSelectionPointerId
            && _pointerSelectionAnchor is int selectionAnchor)
        {
            if (point.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse
                && !point.Properties.IsLeftButtonPressed)
            {
                StopPointerSelection();
                SyncInputWindow();
                RenderViewport();
                return;
            }

            if (_defaultRenderer.TextRenderer.TryHitTest(
                    point.Position.X,
                    point.Position.Y,
                    InputWindow.NativeTextBoxControl.Padding.Left,
                    InputWindow.NativeTextBoxControl.Padding.Top,
                    GetHorizontalOffset(),
                    GetVerticalOffset(),
                    _characterWidth,
                    out var dragAnchor,
                    out _))
            {
                SetProjectedDocumentSelection(
                    new TextSelection(selectionAnchor, dragAnchor.Position.Offset),
                    dragAnchor);
                RequestPointerRender();
            }

            args.Handled = true;
            return;
        }

        if (!_defaultRenderer.TextRenderer.TryHitTest(
                point.Position.X,
                point.Position.Y,
                InputWindow.NativeTextBoxControl.Padding.Left,
                InputWindow.NativeTextBoxControl.Padding.Top,
                GetHorizontalOffset(),
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

    private void SetProjectedDocumentSelection(
        TextSelection selection,
        DocumentAnchor activeAnchor)
    {
        _document.SetCaretSet(new TextCaretSet(new[]
        {
            new TextCaretState(
                selection,
                TextBlockSelectionOperations.GetDisplayColumn(
                    Snapshot,
                    selection.CaretPosition,
                    TabDisplaySize),
                activeAnchor.Affinity,
                preferredHorizontalOffset: null)
        }));
    }

    private void OnViewportChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (!IsProjectedTextSurface && !_synchronizingProjectedScroll)
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
                GetHorizontalOffset(),
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
        if (_renderer is not null && !IsProjectedTextSurface)
        {
            // A custom renderer owns the complete document surface too. Its
            // caret contract is used for input placement, so native text is
            // still restricted to the transparent IME window.
            var customNativeTextBox = InputWindow.NativeTextBoxControl;
            customNativeTextBox.Opacity = 0;
            customNativeTextBox.Foreground = new SolidColorBrush(Colors.Transparent);
            customNativeTextBox.SelectionHighlightColor = new SolidColorBrush(Colors.Transparent);
            customNativeTextBox.SelectionHighlightColorWhenNotFocused = new SolidColorBrush(Colors.Transparent);
            ScrollViewer.SetHorizontalScrollBarVisibility(customNativeTextBox, ScrollBarVisibility.Hidden);
            ScrollViewer.SetVerticalScrollBarVisibility(customNativeTextBox, ScrollBarVisibility.Hidden);
            GutterDrawingSurface.Visibility = Visibility.Collapsed;
            TextDrawingSurface.Visibility = Visibility.Collapsed;
            ProjectedVerticalScrollBar.Visibility = Visibility.Collapsed;
            _projectedVerticalOffset = 0;
            _completionRequested = false;
            HideCompletionPopup();
            _hoverPosition = -1;
            HideTooltipPopup();
            return;
        }

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

    private double GetHorizontalOffset() => IsProjectedTextSurface
        ? 0
        : _scrollViewer?.HorizontalOffset ?? 0;

    private void OnProjectedVerticalScrollChanged(
        object sender,
        RangeBaseValueChangedEventArgs args)
    {
        if (!IsProjectedTextSurface || _synchronizingProjectedScroll)
        {
            return;
        }

        _projectedVerticalOffset = args.NewValue;

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
        }
        finally
        {
            _synchronizingProjectedScroll = false;
        }
    }

    private bool IsCompletionPopupOpen => CompletionPopup.IsOpen;

}
