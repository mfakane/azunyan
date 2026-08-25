using Azunyan.Core;
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
public sealed partial class AzunyanEditorView : UserControl
{
    private readonly AzunyanEditorRenderer _defaultRenderer;
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
    private IReadOnlyList<string> _completionTriggerCharacters = Array.Empty<string>();
    private int _hoverPosition = -1;

    public AzunyanEditorView()
    {
        InitializeComponent();
        _colorScheme = AzunyanColorScheme.Default;
        _defaultRenderer = new AzunyanEditorRenderer(
            GutterDrawingSurface,
            TextDrawingSurface);
        _defaultRenderer.LayoutInvalidated += OnRendererLayoutInvalidated;
        _providerScheduler = new EditorProviderScheduler(_providers);
        _renderer = _defaultRenderer;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ProjectedSurfaceHost.SizeChanged += OnSizeChanged;
        InputEditor.TextChanged += OnInputTextChanged;
        InputEditor.DocumentChanged += OnInputDocumentChanged;
        InputEditor.SelectionChanged += OnInputSelectionChanged;
        InputEditor.CompositionChanged += OnInputCompositionChanged;
        InputEditor.AddHandler(
            UIElement.KeyDownEvent,
            new KeyEventHandler(OnInputKeyDown),
            true);
        InputEditor.PointerPressed += OnInputPointerPressed;
        InputEditor.PointerMoved += OnInputPointerMoved;
        InputEditor.PointerExited += OnInputPointerExited;
        CompletionList.ItemClick += OnCompletionItemClick;
        ProjectedVerticalScrollBar.ValueChanged += OnProjectedVerticalScrollChanged;
        InputEditor.AllowDrop = true;
        InputEditor.IsSpellCheckEnabled = false;
        InputEditor.IsTextPredictionEnabled = false;
        ApplyColorScheme();
        UpdateTextSurfaceMode();
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

    public static readonly DependencyProperty AcceptsReturnProperty =
        DependencyProperty.Register(
            nameof(AcceptsReturn),
            typeof(bool),
            typeof(AzunyanEditorView),
            new PropertyMetadata(true, OnAcceptsReturnChanged));

    public event TextChangedEventHandler? TextChanged
    {
        add => InputEditor.TextChanged += value;
        remove => InputEditor.TextChanged -= value;
    }

    public event RoutedEventHandler? SelectionChanged
    {
        add => InputEditor.SelectionChanged += value;
        remove => InputEditor.SelectionChanged -= value;
    }

    public event EventHandler<DocumentChangedEventArgs>? DocumentChanged
    {
        add => InputEditor.DocumentChanged += value;
        remove => InputEditor.DocumentChanged -= value;
    }

    /// <summary>
    /// Raised when the native text service starts, updates, or ends an IME
    /// composition. The projected renderer consumes the same state and draws
    /// its transient composition decoration from the current snapshot.
    /// </summary>
    public event EventHandler? CompositionChanged
    {
        add => InputEditor.CompositionChanged += value;
        remove => InputEditor.CompositionChanged -= value;
    }

    public Document Document => InputEditor.Document;

    public TextSnapshot Snapshot => InputEditor.Snapshot;

    public bool IsComposing => InputEditor.IsComposing;

    public TextRange? CompositionRange => InputEditor.CompositionRange;

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

    internal AzunyanEditorControl InputHost => InputEditor;

    public string Text
    {
        get => InputEditor.Text;
        set => InputEditor.Text = value;
    }

    public int SelectionStart
    {
        get => InputEditor.SelectionStart;
        set => InputEditor.SelectionStart = value;
    }

    public int SelectionLength
    {
        get => InputEditor.SelectionLength;
        set => InputEditor.SelectionLength = value;
    }

    public string SelectedText
    {
        get => InputEditor.SelectedText;
        set => InputEditor.SelectedText = value;
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

    public void SetText(string text) => InputEditor.SetText(text);

    public void SetDocumentSelection(TextSelection selection) => InputEditor.SetDocumentSelection(selection);

    public void ReplaceDocumentRange(TextRange range, string replacement) =>
        InputEditor.ReplaceDocumentRange(range, replacement);

    public bool UndoDocument() => InputEditor.UndoDocument();

    public bool RedoDocument() => InputEditor.RedoDocument();

    public void CutSelectionToClipboard() => InputEditor.CutSelectionToClipboard();

    public void CopySelectionToClipboard() => InputEditor.CopySelectionToClipboard();

    public void PasteFromClipboard() => InputEditor.PasteFromClipboard();

    public void SelectAll() => InputEditor.SelectAll();

    public void Select(int start, int length) => InputEditor.Select(start, length);

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
                && fold.Range.Contains(InputEditor.Document.Selection.CaretPosition))
            {
                InputEditor.SetDocumentSelection(TextSelection.Caret(fold.Range.Start));
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

    public new bool Focus(FocusState value) => InputEditor.Focus(value);

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new AzunyanEditorViewAutomationPeer(this);

    private static void OnRenderPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((AzunyanEditorView)sender).RenderViewport();
    }

    private static void OnTextWrappingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (AzunyanEditorView)sender;
        view.InputEditor.TextWrapping = (TextWrapping)args.NewValue;
        view.UpdateTextSurfaceMode();
        view.RenderViewport();
    }

    private static void OnAcceptsReturnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((AzunyanEditorView)sender).InputEditor.AcceptsReturn = (bool)args.NewValue;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _scrollViewer = FindDescendant<ScrollViewer>(InputEditor);
        if (_scrollViewer is not null)
        {
            _scrollViewer.ViewChanged += OnViewportChanged;
        }

        UpdateTextSurfaceMode();
        RenderViewport();
        RequestProviderResults(true, true, true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _providerScheduler.CancelAll();
        if (_scrollViewer is not null)
        {
            _scrollViewer.ViewChanged -= OnViewportChanged;
            _scrollViewer = null;
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => RenderViewport();

    private void OnInputTextChanged(object sender, TextChangedEventArgs args)
    {
        RenderViewport();
        if (!InputEditor.IsComposing)
        {
            RequestProviderResults(true, true, true, requestCompletion: _completionRequested);
        }
    }

    private void OnInputDocumentChanged(
        object? sender,
        DocumentChangedEventArgs args)
    {
        _defaultRenderer.TextRenderer.NotifyDocumentChanged(args);
        if (!InputEditor.IsComposing
            && !_applyingCompletion
            && InputEditor.FocusState != FocusState.Unfocused)
        {
            _explicitCompletionRequested = false;
            _completionRequested = args.Kind == DocumentChangeKind.Edit
                && args.Change.IsInsertion
                && IsCompletionTrigger(args.NewSnapshot.Text, args.NewSelection.CaretPosition);
        }
    }

    private void OnInputCompositionChanged(object? sender, EventArgs args)
    {
        _completionRequested = false;
        _explicitCompletionRequested = false;
        HideCompletionPopup();
        RenderViewport();
        if (!InputEditor.IsComposing)
        {
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

    private void OnInputSelectionChanged(object sender, RoutedEventArgs args)
    {
        RenderViewport();
        if (!InputEditor.IsComposing)
        {
            RequestProviderResults(false, false, true, requestCompletion: _completionRequested);
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

        var point = args.GetCurrentPoint(InputEditor);
        if (point.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse
            && !point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (!_defaultRenderer.TextRenderer.TryHitTest(
                point.Position.X,
                point.Position.Y,
                InputEditor.Padding.Left,
                InputEditor.Padding.Top,
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
            InputEditor.SetDocumentSelection(TextSelection.Caret(anchor.Position.Offset));
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
            InputEditor.SetDocumentSelection(TextSelection.Caret(anchor.Position.Offset));
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

        var point = args.GetCurrentPoint(InputEditor);
        if (!_defaultRenderer.TextRenderer.TryHitTest(
                point.Position.X,
                point.Position.Y,
                InputEditor.Padding.Left,
                InputEditor.Padding.Top,
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

    private void RenderViewport()
    {
        if (!IsLoaded)
        {
            return;
        }

        UpdateTextMetrics();

        var lineIndex = InputEditor.Snapshot.Lines;
        var lineCount = lineIndex.LineCount;
        var verticalOffset = GetVerticalOffset();
        var viewportAnchor = DocumentAnchor.Before(0);
        var offsetWithinRow = 0d;
        var preserveViewport = IsProjectedTextSurface
            && _defaultRenderer.TextRenderer.TryGetViewportAnchor(
                InputEditor.Snapshot,
                verticalOffset,
                out viewportAnchor,
                out offsetWithinRow);
        var horizontalOffset = _scrollViewer?.HorizontalOffset ?? 0;
        var viewportWidth = Math.Max(1, ProjectedSurfaceHost.ActualWidth);
        var viewportHeight = Math.Max(1, ProjectedSurfaceHost.ActualHeight);
        var firstVisibleLine = Math.Clamp(
            (int)Math.Floor(verticalOffset / _lineHeight) - 1,
            0,
            Math.Max(0, lineCount - 1));
        var lastVisibleLine = Math.Clamp(
            (int)Math.Ceiling((verticalOffset + viewportHeight) / _lineHeight) + 1,
            0,
            Math.Max(0, lineCount - 1));

        var currentFrame = GetCurrentFrame();
        var digits = Math.Max(1, lineCount.ToString().Length);
        var providerGutter = currentFrame?.Viewport?.Gutter;
        var supportsLogicalLineGutter = IsProjectedTextSurface;
        var hasProviderGutter = supportsLogicalLineGutter && providerGutter is { Count: > 0 };
        var providerGutterDigits = hasProviderGutter
            ? providerGutter!.Max(item => item.Text.Length)
            : 0;
        var showLogicalLineNumbers = supportsLogicalLineGutter && ShowLineNumbers;
        var gutterWidth = showLogicalLineNumbers || hasProviderGutter
            ? Math.Max(32, (Math.Max(digits, providerGutterDigits) * _characterWidth) + 16)
            : 0;
        GutterColumn.Width = new GridLength(gutterWidth);
        GutterCanvas.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, gutterWidth, viewportHeight)
        };
        GutterDrawingSurface.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, gutterWidth, viewportHeight)
        };
        TextRenderLayer.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, viewportWidth, viewportHeight)
        };
        TextDrawingSurface.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, viewportWidth, viewportHeight)
        };
        RenderOverlay.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, viewportWidth, viewportHeight)
        };
        GutterCanvas.Children.Clear();
        TextRenderLayer.Children.Clear();
        RenderOverlay.Children.Clear();

        var context = new AzunyanEditorRenderContext(
            InputEditor.Snapshot,
            InputEditor.Document.Selection,
            InputEditor.CompositionRange,
            _colorScheme,
            GutterCanvas,
            TextRenderLayer,
            RenderOverlay,
            _lineHeight,
            _characterWidth,
            verticalOffset,
            horizontalOffset,
            gutterWidth,
            viewportWidth,
            viewportHeight,
            firstVisibleLine,
            lastVisibleLine,
            InputEditor.FontFamily,
            InputEditor.FontSize,
            InputEditor.Padding.Left,
            InputEditor.Padding.Top,
            showLogicalLineNumbers,
            TextWrapping,
            _collapsedFoldIds,
            currentFrame);
        _renderer?.Render(context);
        CreateProjectedAutomationChildren();
        if (preserveViewport
            && !_preservingViewport
            && _defaultRenderer.TextRenderer.TryGetViewportOffset(
                viewportAnchor,
                offsetWithinRow,
                out var restoredOffset)
            && Math.Abs(restoredOffset - GetVerticalOffset()) > 0.5)
        {
            _projectedVerticalOffset = restoredOffset;
            _preservingViewport = true;
            try
            {
                RenderViewport();
            }
            finally
            {
                _preservingViewport = false;
            }

            return;
        }

        UpdateProjectedScrollExtent(viewportHeight);
        UpdateCompletionPopup();
        UpdateTooltipPopup();
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
            return;
        }

        foreach (var target in _defaultRenderer.TextRenderer.GetAutomationTargets())
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

        InputEditor.SetDocumentSelection(
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

    private void RequestProviderResults(
        bool requestDocument,
        bool requestViewport,
        bool requestPosition,
        bool requestCompletion = false)
    {
        if (!IsLoaded)
        {
            return;
        }

        var snapshot = InputEditor.Snapshot;
        var selection = InputEditor.Document.Selection;
        var currentFrame = GetCurrentFrame();

        if (requestDocument)
        {
            _providerFrame = new EditorProviderFrame(snapshot, selection);
            RenderViewport();
            _ = ApplyDocumentProviderResultAsync(
                _providerScheduler.RequestDocumentAsync(snapshot, selection));
        }

        if (requestViewport)
        {
            _ = ApplyViewportProviderResultAsync(
                _providerScheduler.RequestViewportAsync(
                    snapshot,
                    GetVisibleDocumentRange(),
                    selection));
        }

        if (requestPosition)
        {
            _providerFrame = new EditorProviderFrame(
                snapshot,
                selection,
                currentFrame?.Document,
                currentFrame?.Viewport);
            RenderViewport();
            _ = ApplyPositionProviderResultAsync(
                _providerScheduler.RequestPositionAsync(
                    snapshot,
                    selection.CaretPosition,
                    selection,
                    includeCompletion: requestCompletion));
        }
    }

    private async Task ApplyDocumentProviderResultAsync(Task<DocumentProviderResults?> request)
    {
        DocumentProviderResults? result;
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
            if (!IsLoaded || !ReferenceEquals(result.Snapshot, InputEditor.Snapshot))
            {
                return;
            }

            var currentFrame = GetCurrentFrame();
            PublishProviderFrame(new EditorProviderFrame(
                result.Snapshot,
                InputEditor.Document.Selection,
                result,
                currentFrame?.Viewport,
                currentFrame?.Position));
        });
    }

    private async Task ApplyViewportProviderResultAsync(Task<ViewportProviderResults?> request)
    {
        ViewportProviderResults? result;
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
            if (!IsLoaded || !ReferenceEquals(result.Context.Snapshot, InputEditor.Snapshot))
            {
                return;
            }

            var currentFrame = GetCurrentFrame();
            PublishProviderFrame(new EditorProviderFrame(
                result.Context.Snapshot,
                InputEditor.Document.Selection,
                currentFrame?.Document,
                result,
                currentFrame?.Position));
        });
    }

    private async Task ApplyPositionProviderResultAsync(Task<PositionProviderResults?> request)
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
            if (!IsLoaded || !ReferenceEquals(result.Context.Snapshot, InputEditor.Snapshot))
            {
                return;
            }

            var currentFrame = GetCurrentFrame();
            PublishProviderFrame(new EditorProviderFrame(
                result.Context.Snapshot,
                InputEditor.Document.Selection,
                currentFrame?.Document,
                currentFrame?.Viewport,
                result));
        });
    }

    private void PublishProviderFrame(EditorProviderFrame frame)
    {
        _providerFrame = frame;
        RenderViewport();
        ProviderFrameChanged?.Invoke(this, new EditorProviderFrameEventArgs(frame));
        ProviderResultsChanged?.Invoke(
            this,
            new EditorProviderResultsEventArgs(frame.ToLegacyResults()));
    }

    private EditorProviderFrame? GetCurrentFrame() =>
        _providerFrame is { } frame
        && ReferenceEquals(frame.Snapshot, InputEditor.Snapshot)
            ? frame
            : null;

    private TextRange GetVisibleDocumentRange()
    {
        UpdateTextMetrics();
        var snapshot = InputEditor.Snapshot;
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

    internal TextSelection AutomationSelection => InputEditor.Document.Selection;

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
                InputEditor.Padding.Left,
                InputEditor.Padding.Top,
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
            || range.End > InputEditor.Snapshot.Length
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
            FontFamily = InputEditor.FontFamily,
            FontSize = InputEditor.FontSize
        };
        sample.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _lineHeight = Math.Max(1, sample.DesiredSize.Height);

        sample.Text = "0";
        sample.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _characterWidth = Math.Max(1, sample.DesiredSize.Width);
    }

    private void ApplyColorScheme()
    {
        RootGrid.Background = new SolidColorBrush(_colorScheme.EditorBackground);
        InputEditor.Background = new SolidColorBrush(_colorScheme.EditorBackground);
        GutterCanvas.Background = new SolidColorBrush(_colorScheme.GutterBackground);

        CompletionBorder.Background = new SolidColorBrush(_colorScheme.PopupBackground);
        CompletionBorder.BorderBrush = new SolidColorBrush(_colorScheme.PopupBorder);
        CompletionList.Foreground = new SolidColorBrush(_colorScheme.PopupForeground);

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
            InputEditor.Opacity = 0;
            InputEditor.Foreground = new SolidColorBrush(Colors.Transparent);
            InputEditor.SelectionHighlightColor = new SolidColorBrush(Colors.Transparent);
            InputEditor.SelectionHighlightColorWhenNotFocused = new SolidColorBrush(Colors.Transparent);
            GutterDrawingSurface.Visibility = Visibility.Visible;
            TextDrawingSurface.Visibility = Visibility.Visible;
            ScrollViewer.SetHorizontalScrollBarVisibility(InputEditor, ScrollBarVisibility.Hidden);
            ScrollViewer.SetVerticalScrollBarVisibility(InputEditor, ScrollBarVisibility.Hidden);
            ProjectedVerticalScrollBar.Visibility = Visibility.Visible;
            return;
        }

        InputEditor.Opacity = 1;
        InputEditor.ClearValue(Control.ForegroundProperty);
        InputEditor.ClearValue(TextBox.SelectionHighlightColorProperty);
        InputEditor.ClearValue(TextBox.SelectionHighlightColorWhenNotFocusedProperty);
        GutterDrawingSurface.Visibility = Visibility.Collapsed;
        TextDrawingSurface.Visibility = Visibility.Collapsed;
        ScrollViewer.SetHorizontalScrollBarVisibility(InputEditor, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(InputEditor, ScrollBarVisibility.Auto);
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

        _ = ApplyPositionProviderResultAsync(
            _providerScheduler.RequestPositionAsync(
                InputEditor.Snapshot,
                position,
                InputEditor.Document.Selection,
                includeCompletion: false));
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
        if (_completionTriggerCharacters.Count == 0
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
        CompletionPopup.IsOpen = false;
        CompletionList.Items.Clear();
        _displayedCompletionItems = null;
        CompletionList.SelectedIndex = -1;
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
