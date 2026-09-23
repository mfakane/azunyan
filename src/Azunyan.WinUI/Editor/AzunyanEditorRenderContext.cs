using Azunyan.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Azunyan.WinUI;

/// <summary>
/// Internal WinUI adapter that combines an immutable render frame with the
/// control surfaces used by the built-in renderer.
/// </summary>
internal sealed class AzunyanEditorRenderContext
{
    internal AzunyanEditorRenderContext(
        TextSnapshot snapshot,
        TextSelection selection,
        DocumentAnchor primaryCaretAnchor,
        TextBlockSelection? blockSelection,
        TextCaretSet? caretSet,
        TextRange? compositionRange,
        AzunyanColorScheme colorScheme,
        Canvas gutterLayer,
        Canvas textLayer,
        Canvas overlayLayer,
        double lineHeight,
        double characterWidth,
        double verticalOffset,
        double horizontalOffset,
        double gutterWidth,
        double viewportWidth,
        double viewportHeight,
        int firstVisibleLine,
        int lastVisibleLine,
        FontFamily fontFamily,
        double fontSize,
        double contentLeft,
        double contentTop,
        bool showLineNumbers,
        TextWrapping textWrapping,
        int tabDisplaySize,
        IReadOnlySet<string> collapsedFoldIds,
        IReadOnlyList<FoldRange> folds,
        EditorProviderFrame? providerFrame,
        Action<string>? toggleFold)
    {
        Snapshot = snapshot;
        Selection = selection;
        PrimaryCaretAnchor = primaryCaretAnchor;
        BlockSelection = blockSelection;
        CaretSet = caretSet;
        CompositionRange = compositionRange;
        ColorScheme = colorScheme ?? throw new ArgumentNullException(nameof(colorScheme));
        GutterLayer = gutterLayer;
        TextLayer = textLayer;
        OverlayLayer = overlayLayer;
        LineHeight = lineHeight;
        CharacterWidth = characterWidth;
        VerticalOffset = verticalOffset;
        HorizontalOffset = horizontalOffset;
        GutterWidth = gutterWidth;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        FirstVisibleLine = firstVisibleLine;
        LastVisibleLine = lastVisibleLine;
        FontFamily = fontFamily;
        FontSize = fontSize;
        ContentLeft = contentLeft;
        ContentTop = contentTop;
        ShowLineNumbers = showLineNumbers;
        TextWrapping = textWrapping;
        TabDisplaySize = tabDisplaySize;
        CollapsedFoldIds = collapsedFoldIds;
        Folds = folds;
        ProviderFrame = providerFrame;
        ProviderResults = providerFrame?.ToLegacyResults();
        ToggleFold = toggleFold;
    }

    internal AzunyanEditorRenderContext(
        AzunyanEditorRenderFrame frame,
        Canvas gutterLayer,
        Canvas textLayer,
        Canvas overlayLayer,
        Action<string>? toggleFold = null)
        : this(
            frame.Snapshot,
            frame.Selection,
            frame.PrimaryCaretAnchor,
            frame.BlockSelection,
            frame.CaretSet,
            frame.CompositionRange,
            frame.ColorScheme,
            gutterLayer,
            textLayer,
            overlayLayer,
            frame.LineHeight,
            frame.CharacterWidth,
            frame.VerticalOffset,
            frame.HorizontalOffset,
            frame.GutterWidth,
            frame.ViewportWidth,
            frame.ViewportHeight,
            frame.FirstVisibleLine,
            frame.LastVisibleLine,
            frame.FontFamily,
            frame.FontSize,
            frame.ContentLeft,
            frame.ContentTop,
            frame.ShowLineNumbers,
            frame.TextWrapping,
            frame.TabDisplaySize,
            frame.CollapsedFoldIds,
            frame.Folds,
            frame.ProviderFrame,
            toggleFold)
    {
        DiagnosticInputSequence = frame.DiagnosticInputSequence;
        DiagnosticInputStarted = frame.DiagnosticInputStarted;
        DiagnosticSink = frame.DiagnosticSink;
    }

    public TextSnapshot Snapshot { get; }

    public TextSelection Selection { get; }

    public DocumentAnchor PrimaryCaretAnchor { get; }

    public TextBlockSelection? BlockSelection { get; }

    public TextCaretSet? CaretSet { get; }

    /// <summary>
    /// The transient UTF-16 range currently owned by the native IME text
    /// service. It is not part of provider state and must not be persisted as
    /// document content.
    /// </summary>
    public TextRange? CompositionRange { get; }

    public AzunyanColorScheme ColorScheme { get; }

    public Canvas GutterLayer { get; }

    public Canvas TextLayer { get; }

    public Canvas OverlayLayer { get; }

    public double LineHeight { get; }

    public double CharacterWidth { get; }

    public double VerticalOffset { get; }

    public double HorizontalOffset { get; }

    public double GutterWidth { get; }

    public double ViewportWidth { get; }

    public double ViewportHeight { get; }

    public int FirstVisibleLine { get; }

    public int LastVisibleLine { get; }

    public FontFamily FontFamily { get; }

    public double FontSize { get; }

    public double ContentLeft { get; }

    public double ContentTop { get; }

    public bool ShowLineNumbers { get; }

    public TextWrapping TextWrapping { get; }

    public int TabDisplaySize { get; }

    public IReadOnlySet<string> CollapsedFoldIds { get; }

    public IReadOnlyList<FoldRange> Folds { get; }

    internal Action<string>? ToggleFold { get; }

    internal long DiagnosticInputSequence { get; }

    internal long DiagnosticInputStarted { get; }

    internal Action<string>? DiagnosticSink { get; }

    /// <summary>
    /// The immutable provider channels used to build this render pass. A
    /// channel may be absent while its independent request is still running.
    /// </summary>
    public EditorProviderFrame? ProviderFrame { get; }

    public DocumentProviderResults? DocumentResults => ProviderFrame?.Document;

    public ViewportProviderResults? ViewportResults => ProviderFrame?.Viewport;

    public PositionProviderResults? PositionResults => ProviderFrame?.Position;

    /// <summary>
    /// The newest provider results for this viewport request. A renderer may
    /// use the syntax, decoration, gutter, tooltip, or completion data as
    /// appropriate for its own visual layer.
    /// </summary>
    public EditorProviderResults? ProviderResults { get; }

    public TextRange GetLineRange(int line) => Snapshot.Lines.GetLineRange(line);
}

internal interface ICanvasEditorRenderer
{
    void Render(AzunyanEditorRenderContext context);
}

public interface IAzunyanEditorRenderer
{
    void Render(AzunyanEditorRenderFrame frame);

    /// <summary>
    /// Gets the rendered caret rectangle in the editor host's coordinate
    /// space. The view uses this rectangle to place its native IME window.
    /// </summary>
    bool TryGetCaretRect(DocumentAnchor anchor, out Windows.Foundation.Rect rect);
}

public enum AzunyanEditorNavigationKind
{
    Up,
    Down,
    PageUp,
    PageDown,
    SmartHome,
    End
}

public readonly record struct AzunyanEditorNavigationRequest(
    TextCaretSet Carets,
    AzunyanEditorNavigationKind Kind,
    bool ExtendSelection,
    int TabDisplaySize);

/// <summary>
/// Optional capability for custom renderers whose visual rows differ from
/// logical document lines.
/// </summary>
public interface IAzunyanEditorNavigationGeometry
{
    bool TryNavigate(
        AzunyanEditorNavigationRequest request,
        out TextCaretSet result);
}
