using Azunyan.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;

namespace Azunote;

/// <summary>
/// Reusable editor host. The inner TextBox remains the native text-service and
/// IME host, while the gutter and overlay are independent rendering layers.
/// The default renderer keeps native text rendering enabled for reliable IME
/// behavior; a future renderer can take over the text layer for syntax text,
/// selections, decorations, and inlay hints without changing the document or
/// input contract.
/// </summary>
public sealed partial class AzunyanEditorView : UserControl
{
    private readonly LineNumberRenderer _defaultRenderer = new();
    private readonly EditorProviderSet _providers = new()
    {
        Syntax = new AzunoteSyntaxProvider(),
        Completion = new AzunoteCompletionProvider()
    };
    private readonly EditorProviderCoordinator _providerCoordinator;
    private ScrollViewer? _scrollViewer;
    private double _lineHeight = 18;
    private double _characterWidth = 8;
    private IAzunyanEditorRenderer? _renderer;
    private EditorProviderResults? _providerResults;

    public AzunyanEditorView()
    {
        InitializeComponent();
        _providerCoordinator = new EditorProviderCoordinator(_providers);
        _renderer = _defaultRenderer;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        InputEditor.TextChanged += OnInputTextChanged;
        InputEditor.SelectionChanged += OnInputSelectionChanged;
        InputEditor.AllowDrop = true;
        InputEditor.IsSpellCheckEnabled = false;
        InputEditor.IsTextPredictionEnabled = false;
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

    public Document Document => InputEditor.Document;

    public TextSnapshot Snapshot => InputEditor.Snapshot;

    /// <summary>
    /// Providers are called with immutable snapshots and are safe to replace
    /// while the editor is running. Call <see cref="RefreshProviders"/> after
    /// mutating this set.
    /// </summary>
    public EditorProviderSet Providers => _providers;

    public EditorProviderResults? ProviderResults => _providerResults;

    public event EventHandler<EditorProviderResultsEventArgs>? ProviderResultsChanged;

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
            RenderViewport();
        }
    }

    public void SetText(string text) => InputEditor.SetText(text);

    public void SetDocumentSelection(TextSelection selection) => InputEditor.SetDocumentSelection(selection);

    public bool UndoDocument() => InputEditor.UndoDocument();

    public bool RedoDocument() => InputEditor.RedoDocument();

    public void CutSelectionToClipboard() => InputEditor.CutSelectionToClipboard();

    public void CopySelectionToClipboard() => InputEditor.CopySelectionToClipboard();

    public void PasteFromClipboard() => InputEditor.PasteFromClipboard();

    public void SelectAll() => InputEditor.SelectAll();

    public void Select(int start, int length) => InputEditor.Select(start, length);

    public void RefreshProviders() => RequestProviderResults();

    public new bool Focus(FocusState value) => InputEditor.Focus(value);

    private static void OnRenderPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((AzunyanEditorView)sender).RenderViewport();
    }

    private static void OnTextWrappingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (AzunyanEditorView)sender;
        view.InputEditor.TextWrapping = (TextWrapping)args.NewValue;
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

        RenderViewport();
        RequestProviderResults();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _providerCoordinator.Cancel();
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
        RequestProviderResults();
    }

    private void OnInputSelectionChanged(object sender, RoutedEventArgs args)
    {
        RenderViewport();
        RequestProviderResults();
    }

    private void OnViewportChanged(object? sender, ScrollViewerViewChangedEventArgs args) => RenderViewport();

    private void RenderViewport()
    {
        if (!IsLoaded)
        {
            return;
        }

        UpdateTextMetrics();

        var lineIndex = InputEditor.Snapshot.Lines;
        var lineCount = lineIndex.LineCount;
        var verticalOffset = _scrollViewer?.VerticalOffset ?? 0;
        var horizontalOffset = _scrollViewer?.HorizontalOffset ?? 0;
        var viewportHeight = Math.Max(1, EditorHost.ActualHeight);
        var firstVisibleLine = Math.Clamp(
            (int)Math.Floor(verticalOffset / _lineHeight) - 1,
            0,
            Math.Max(0, lineCount - 1));
        var lastVisibleLine = Math.Clamp(
            (int)Math.Ceiling((verticalOffset + viewportHeight) / _lineHeight) + 1,
            0,
            Math.Max(0, lineCount - 1));

        var digits = Math.Max(1, lineCount.ToString().Length);
        var providerGutter = _providerResults?.Gutter;
        var providerGutterDigits = providerGutter is { Count: > 0 }
            ? providerGutter.Max(item => item.Text.Length)
            : 0;
        var gutterWidth = ShowLineNumbers || providerGutter is { Count: > 0 }
            ? Math.Max(32, (Math.Max(digits, providerGutterDigits) * _characterWidth) + 16)
            : 0;
        GutterColumn.Width = new GridLength(gutterWidth);
        GutterCanvas.Width = gutterWidth;
        GutterCanvas.Height = viewportHeight;
        RenderOverlay.Width = Math.Max(1, EditorHost.ActualWidth);
        RenderOverlay.Height = viewportHeight;
        TextRenderLayer.Width = Math.Max(1, EditorHost.ActualWidth);
        TextRenderLayer.Height = viewportHeight;
        GutterCanvas.Children.Clear();
        TextRenderLayer.Children.Clear();
        RenderOverlay.Children.Clear();

        var context = new AzunyanEditorRenderContext(
            InputEditor.Snapshot,
            InputEditor.Document.Selection,
            GutterCanvas,
            TextRenderLayer,
            RenderOverlay,
            _lineHeight,
            _characterWidth,
            verticalOffset,
            horizontalOffset,
            gutterWidth,
            firstVisibleLine,
            lastVisibleLine,
            InputEditor.FontFamily,
            InputEditor.FontSize,
            InputEditor.Padding.Top,
            ShowLineNumbers,
            _providerResults);
        _renderer?.Render(context);
    }

    private void RequestProviderResults()
    {
        if (!IsLoaded)
        {
            return;
        }

        var snapshot = InputEditor.Snapshot;
        var selection = InputEditor.Document.Selection;
        _ = ApplyProviderResultsAsync(_providerCoordinator.RequestAsync(
            snapshot,
            selection.CaretPosition,
            selection));
    }

    private async Task ApplyProviderResultsAsync(Task<EditorProviderResults?> request)
    {
        EditorProviderResults? results;
        try
        {
            results = await request.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A third-party provider must not take down the editor surface.
            return;
        }

        if (results is null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsLoaded || !ReferenceEquals(results.Context.Snapshot, InputEditor.Snapshot))
            {
                return;
            }

            _providerResults = results;
            RenderViewport();
            ProviderResultsChanged?.Invoke(this, new EditorProviderResultsEventArgs(results));
        });
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
